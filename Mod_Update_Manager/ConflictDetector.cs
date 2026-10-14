using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace mod_update_manager
{
    /// <summary>
    /// Detects potential conflicts between mods, in two tiers the Conflicts tab keeps apart:
    /// GUID-confirmed (two plugin DLLs register the same [BepInPlugin] GUID, read from their
    /// metadata by PluginMetadataReader) and name-heuristic (mod names that match a pattern or a
    /// hand-listed pair, which a renamed folder or a translated name can get wrong).
    /// </summary>
    public class ConflictDetector
    {
        public class ConflictInfo
        {
            public string ModA { get; set; }
            public string ModB { get; set; }
            public ConflictType Type { get; set; }
            public string Description { get; set; }
            public ConflictSeverity Severity { get; set; }
            public ConflictSource Source { get; set; }
        }

        public enum ConflictType
        {
            SameFunctionality,
            FileOverlap,
            VersionMismatch,
            LoadOrderIssue,
            ApiVersionMismatch,
            DuplicatePluginGuid
        }

        public enum ConflictSeverity
        {
            Info,
            Warning,
            Critical
        }

        public enum ConflictSource
        {
            /// <summary>Two DLLs register the literal same BepInEx plugin GUID.</summary>
            GuidConfirmed,
            /// <summary>Matched on mod names only; shown with "(heuristic - check manually)".</summary>
            NameHeuristic
        }

        private List<(string modNamePattern, string functionality)> _functionalities;
        private Dictionary<string, List<string>> _knownConflicts;

        // DrawModConflicts calls DetectConflicts on every OnGUI pass and the GUID tier reads every
        // plugin DLL from disk, so the whole result is cached per scanned mod list.
        // UpdateChecker.ScanMods replaces that list and calls Invalidate, next to
        // DependencyValidator.Invalidate.
        private static List<ConflictInfo> _cache;
        private static List<InstalledModInfo> _cacheFor;

        public ConflictDetector()
        {
            _functionalities = new List<(string, string)>();
            _knownConflicts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            InitializeKnownConflicts();
            InitializeFunctionalities();
        }

        /// <summary>Drops the cached result so the next read rescans the plugins folder.</summary>
        public static void Invalidate()
        {
            _cache = null;
            _cacheFor = null;
        }

        /// <summary>
        /// Check for conflicts between installed mods: GUID-confirmed ones first, then the name
        /// heuristics. Never throws; a failed GUID scan degrades to the heuristic tier alone.
        /// </summary>
        public List<ConflictInfo> DetectConflicts(List<InstalledModInfo> mods)
        {
            if (_cache != null && ReferenceEquals(_cacheFor, mods))
                return _cache;

            var guidTier = new List<ConflictInfo>();
            string guidSummary;
            try
            {
                guidTier = DetectDuplicatePluginGuids(out guidSummary);
            }
            catch (Exception ex)
            {
                guidSummary = $"GUID scan failed ({ex.Message})";
                try { Plugin.Logger?.LogDebug($"ConflictDetector: the plugin GUID scan threw: {ex}"); } catch { }
            }

            var heuristicTier = new List<ConflictInfo>();
            heuristicTier.AddRange(DetectDuplicateFunctionality(mods));
            heuristicTier.AddRange(DetectKnownConflicts(mods));

            _cache = guidTier.Concat(heuristicTier).ToList();
            _cacheFor = mods;

            // Info, not Debug: with nothing found the tab says only "No conflicts detected!", and
            // this line is what tells "scanned and clean" from "never scanned". Once per scan.
            try { Plugin.Logger?.LogInfo($"Conflict scan: {guidSummary}; {heuristicTier.Count} name-heuristic match(es)."); } catch { }
            return _cache;
        }

        private static List<ConflictInfo> DetectDuplicatePluginGuids(out string summary)
        {
            var pluginsRoot = Path.Combine(Paths.BepInExRootPath, "plugins");
            var guidsByDll = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int scanned = 0, unreadable = 0;
            if (Directory.Exists(pluginsRoot))
            {
                foreach (var dll in Directory.GetFiles(pluginsRoot, "*.dll", SearchOption.AllDirectories))
                {
                    scanned++;
                    var meta = PluginMetadataReader.Read(dll);
                    if (meta == null) { unreadable++; continue; }
                    if (meta.PluginGuids.Count == 0) continue;
                    guidsByDll[dll.Substring(pluginsRoot.Length).TrimStart('\\', '/')] = new List<string>(meta.PluginGuids);
                }
            }

            var collisions = PluginGuidCollisions.Find(guidsByDll);
            summary = $"{scanned} DLL(s) read ({unreadable} unreadable), {guidsByDll.Count} declare a plugin GUID, " +
                      $"{collisions.Count} GUID-confirmed collision(s)";
            return collisions.Select(c => new ConflictInfo
            {
                ModA = c.DllA,
                ModB = c.DllB,
                Type = ConflictType.DuplicatePluginGuid,
                Description = $"Both register the BepInEx plugin GUID \"{c.Guid}\".",
                Severity = ConflictSeverity.Warning,
                Source = ConflictSource.GuidConfirmed
            }).ToList();
        }

        /// <summary>
        /// Check if two specific mods conflict
        /// </summary>
        public ConflictInfo CheckModPair(InstalledModInfo modA, InstalledModInfo modB)
        {
            // Check known conflicts
            if (_knownConflicts.ContainsKey(modA.Name))
            {
                if (_knownConflicts[modA.Name].Any(m => m.Equals(modB.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return new ConflictInfo
                    {
                        ModA = modA.Name,
                        ModB = modB.Name,
                        Type = ConflictType.SameFunctionality,
                        Description = $"{modA.Name} and {modB.Name} provide the same functionality",
                        Severity = ConflictSeverity.Warning,
                        Source = ConflictSource.NameHeuristic
                    };
                }
            }

            // Check for duplicate functionality
            var funcA = GetModFunctionality(modA.Name);
            var funcB = GetModFunctionality(modB.Name);

            if (!string.IsNullOrEmpty(funcA) && funcA == funcB)
            {
                return new ConflictInfo
                {
                    ModA = modA.Name,
                    ModB = modB.Name,
                    Type = ConflictType.SameFunctionality,
                    Description = $"Both mods provide {funcA} functionality",
                    Severity = ConflictSeverity.Warning,
                    Source = ConflictSource.NameHeuristic
                };
            }

            return null;
        }

        /// <summary>
        /// Get recommendations for resolving conflicts
        /// </summary>
        public string GetConflictResolution(ConflictInfo conflict)
        {
            return conflict.Type switch
            {
                ConflictType.SameFunctionality =>
                    $"These mods provide similar functionality. Consider disabling one of them.",
                ConflictType.VersionMismatch =>
                    $"Version compatibility issue detected. Update the mods to the latest version.",
                ConflictType.LoadOrderIssue =>
                    $"Load order may be important. Try reordering the mods.",
                ConflictType.ApiVersionMismatch =>
                    $"API version mismatch. Ensure all mods are updated for the current game version.",
                ConflictType.DuplicatePluginGuid =>
                    "BepInEx loads only one of these, the newest version. If they are two copies of one mod, remove the older; " +
                    "if one is a small copy bundled inside another mod's folder, it is usually harmless.",
                _ => "Unknown conflict type. Please check the mod descriptions."
            };
        }

        private List<ConflictInfo> DetectDuplicateFunctionality(List<InstalledModInfo> mods)
        {
            var conflicts = new List<ConflictInfo>();
            var funcMap = new Dictionary<string, List<InstalledModInfo>>();

            foreach (var mod in mods)
            {
                var func = GetModFunctionality(mod.Name);
                if (!string.IsNullOrEmpty(func))
                {
                    if (!funcMap.ContainsKey(func))
                        funcMap[func] = new List<InstalledModInfo>();

                    funcMap[func].Add(mod);
                }
            }

            foreach (var kvp in funcMap)
            {
                if (kvp.Value.Count > 1)
                {
                    for (int i = 0; i < kvp.Value.Count - 1; i++)
                    {
                        for (int j = i + 1; j < kvp.Value.Count; j++)
                        {
                            conflicts.Add(new ConflictInfo
                            {
                                ModA = kvp.Value[i].Name,
                                ModB = kvp.Value[j].Name,
                                Type = ConflictType.SameFunctionality,
                                Description = $"Both mods provide {kvp.Key} functionality",
                                Severity = ConflictSeverity.Warning,
                                Source = ConflictSource.NameHeuristic
                            });
                        }
                    }
                }
            }

            return conflicts;
        }

        private List<ConflictInfo> DetectKnownConflicts(List<InstalledModInfo> mods)
        {
            var conflicts = new List<ConflictInfo>();
            var modNames = mods.Select(m => m.Name).ToList();

            foreach (var modName in modNames)
            {
                if (_knownConflicts.ContainsKey(modName))
                {
                    var conflictingMods = _knownConflicts[modName];
                    foreach (var conflictingMod in conflictingMods)
                    {
                        if (modNames.Any(m => m.Equals(conflictingMod, StringComparison.OrdinalIgnoreCase)))
                        {
                            conflicts.Add(new ConflictInfo
                            {
                                ModA = modName,
                                ModB = conflictingMod,
                                Type = ConflictType.SameFunctionality,
                                Description = $"Known conflict: {modName} and {conflictingMod}",
                                Severity = ConflictSeverity.Critical,
                                Source = ConflictSource.NameHeuristic
                            });
                        }
                    }
                }
            }

            return conflicts;
        }

        private string GetModFunctionality(string modName)
        {
            var lower = modName.ToLowerInvariant();
            return _functionalities.FirstOrDefault(f => lower.Contains(f.modNamePattern)).functionality;
        }

        private void InitializeKnownConflicts()
        {
            // Hand-listed pairs, matched by mod NAME and therefore shown in the heuristic tier. Its
            // one pair stays name-keyed because UIScale was not on the reference install
            // (2026-09-26), so its GUID could not be read; only CardSizeReduce was.
            _knownConflicts.Add("CardSizeReduce", new List<string> { "UIScale" });
        }

        private void InitializeFunctionalities()
        {
            // Map mod name patterns to functionalities.
            // Patterns must be specific enough to avoid false positives — generic material
            // words like "copper", "metal", "water", "stone", "wood" are intentionally
            // excluded because many complementary mods share those words without conflicting.
            _functionalities.Add(("autosort", "inventory management"));
            _functionalities.Add(("smartinventory", "inventory management"));
            _functionalities.Add(("betterinventory", "inventory management"));
            _functionalities.Add(("irrigation", "water mechanics"));
        }
    }
}
