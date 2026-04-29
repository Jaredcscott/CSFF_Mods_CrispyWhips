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
    // Once injection succeeds, skip redundant re-injection on subsequent NewBlueprintContent.Start calls
    private static bool _injected;

    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        _queued.Clear();
        _warnedMissing.Clear();
        _injected = false;

        foreach (var mod in mods)
        {
            // Priority 1: BlueprintTabs.json config file (declarative mapping)
            var tabsConfigPath = Path.Combine(mod.DirectoryPath, "BlueprintTabs.json");
            if (File.Exists(tabsConfigPath))
            {
                LoadFromTabsConfig(tabsConfigPath, mod.Name);
                continue;
            }

            // Priority 2: Scan blueprint JSONs for tab fields or default to Support
            var cardDir = Path.Combine(mod.DirectoryPath, "CardData");
            if (!Directory.Exists(cardDir)) continue;

            foreach (var file in Directory.GetFiles(cardDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var cardTypeStr = PathUtil.QuickExtractString(json, "CardType");
                    if (cardTypeStr != "7") continue;

                    var uid = PathUtil.QuickExtractString(json, "UniqueID");
                    if (string.IsNullOrEmpty(uid)) continue;

                    var tabKey = PathUtil.QuickExtractString(json, "BlueprintCardDataCardTabGroup");
                    var subTabKey = PathUtil.QuickExtractString(json, "BlueprintCardDataCardTabSubGroup");

                    _queued.Add((uid, tabKey, subTabKey));
                }
                catch { }
            }
        }

        if (_queued.Count == 0) return;

        Log.Info($"BlueprintInjector: {_queued.Count} blueprints queued for tab injection");
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
            // Simple JSON parser for { "key": ["val1", "val2"], ... }
            // Using string parsing since we can't depend on LitJson/Newtonsoft
            int pos = 0;
            while (pos < json.Length)
            {
                // Find next key (tab LocalizationKey)
                var keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;
                var keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                var tabKey = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                // Find the array start
                var arrStart = json.IndexOf('[', keyEnd);
                if (arrStart < 0) break;
                var arrEnd = json.IndexOf(']', arrStart);
                if (arrEnd < 0) break;

                // Extract blueprint IDs from the array
                var arrContent = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
                int aPos = 0;
                while (aPos < arrContent.Length)
                {
                    var vStart = arrContent.IndexOf('"', aPos);
                    if (vStart < 0) break;
                    var vEnd = arrContent.IndexOf('"', vStart + 1);
                    if (vEnd < 0) break;
                    var bpId = arrContent.Substring(vStart + 1, vEnd - vStart - 1);

                    _queued.Add((bpId, tabKey, null));
                    aPos = vEnd + 1;
                }

                pos = arrEnd + 1;
            }

            Log.Debug($"BlueprintInjector: loaded tab config from {modName}/BlueprintTabs.json");
        }
        catch (Exception ex)
        {
            Log.Error($"BlueprintInjector: failed to read {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Called from NewBlueprintContent.Start prefix — injects all queued blueprints
    /// on the first call (tabs now guaranteed to exist), then skips subsequent calls.
    /// </summary>
    public static void InjectFromUI()
    {
        if (_injected || _queued.Count == 0) return;

        try
        {
            var allData = Loading.LoadOrchestrator.GetAllData();
            DoInject(allData);
            _injected = true;
        }
        catch (Exception ex)
        {
            Log.Error($"BlueprintInjector.InjectFromUI: {ex}");
        }
    }

    private static void DoInject(IEnumerable allData)
    {
        // Build lookup: UniqueID → object
        var lookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allData)
        {
            if (item == null) continue;
            if (item is UniqueIDScriptable uid && !string.IsNullOrEmpty(uid.UniqueID))
                lookup[uid.UniqueID] = item;
        }

        // Find all CardTabGroup instances
        var cardTabGroupType = ReflectionCache.FindType("CardTabGroup");
        if (cardTabGroupType == null)
        {
            Log.Warn("BlueprintInjector: CardTabGroup type not found");
            return;
        }

        var findMethod = typeof(Resources).GetMethods()
            .FirstOrDefault(m => m.Name == "FindObjectsOfTypeAll" && m.IsGenericMethod);
        if (findMethod == null) return;

        var generic = findMethod.MakeGenericMethod(cardTabGroupType);
        var tabGroups = generic.Invoke(null, null) as Array;
        if (tabGroups == null || tabGroups.Length == 0) return;

        // Build tab lookup by LocalizationKey
        var tabLookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var tabNameField = AccessTools.Field(cardTabGroupType, "TabName");

        foreach (var tab in tabGroups)
        {
            if (tab == null) continue;
            var tabNameObj = tabNameField?.GetValue(tab);
            if (tabNameObj == null) continue;

            var locType = tabNameObj.GetType();
            // Try field first, then property — LocalizationKey may be either
            string locKey = null;
            var locKeyField = locType.GetField("LocalizationKey",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (locKeyField != null)
            {
                locKey = locKeyField.GetValue(tabNameObj) as string;
            }
            else
            {
                var locKeyProp = locType.GetProperty("LocalizationKey",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                locKey = locKeyProp?.GetValue(tabNameObj) as string;
            }
            if (!string.IsNullOrEmpty(locKey))
                tabLookup[locKey] = tab;
        }

        Log.Debug($"BlueprintInjector: tabLookup built with {tabLookup.Count} entries from {tabGroups.Length} CardTabGroup objects");

        var includedCardsField = AccessTools.Field(cardTabGroupType, "IncludedCards");
        int injected = 0;

        foreach (var (uniqueId, tabKey, subTabKey) in _queued)
        {
            if (!lookup.TryGetValue(uniqueId, out var blueprint))
            {
                if (_warnedMissing.Add(uniqueId))
                    Log.Warn($"BlueprintInjector: blueprint '{uniqueId}' not found in AllData — skipping");
                continue;
            }

            // Try subTabKey first, then tabKey, then fallback to Support
            object targetTab = null;
            if (!string.IsNullOrEmpty(subTabKey))
                tabLookup.TryGetValue(subTabKey, out targetTab);
            if (targetTab == null && !string.IsNullOrEmpty(tabKey))
                tabLookup.TryGetValue(tabKey, out targetTab);
            if (targetTab == null)
                tabLookup.TryGetValue("Tab_1_Survival_Subtab_2_Support_TabName", out targetTab);

            if (targetTab == null)
            {
                if (_warnedMissing.Add($"tab:{uniqueId}"))
                    Log.Warn($"BlueprintInjector: no tab found for blueprint '{uniqueId}' (tabKey='{tabKey}', subTabKey='{subTabKey}') — skipping");
                continue;
            }

            var includedCards = includedCardsField?.GetValue(targetTab) as IList;
            if (includedCards == null)
            {
                Log.Warn($"BlueprintInjector: IncludedCards is null on tab for blueprint '{uniqueId}' — skipping");
                continue;
            }
            if (!includedCards.Contains(blueprint))
            {
                includedCards.Add(blueprint);
                injected++;
            }
        }

        if (injected > 0)
            Log.Info($"BlueprintInjector: injected {injected} blueprints into tabs");
    }
}
