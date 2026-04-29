using System;
using System.Collections.Generic;
using System.Linq;

namespace mod_update_manager
{
    /// <summary>
    /// Detects potential conflicts between mods
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
        }

        public enum ConflictType
        {
            SameFunctionality,
            FileOverlap,
            VersionMismatch,
            LoadOrderIssue,
            ApiVersionMismatch
        }

        public enum ConflictSeverity
        {
            Info,
            Warning,
            Critical
        }

        private List<(string modNamePattern, string functionality)> _functionalities;
        private Dictionary<string, List<string>> _knownConflicts;

        public ConflictDetector()
        {
            _functionalities = new List<(string, string)>();
            _knownConflicts = new Dictionary<string, List<string>>();
            InitializeKnownConflicts();
            InitializeFunctionalities();
        }

        /// <summary>
        /// Check for conflicts between installed mods
        /// </summary>
        public List<ConflictInfo> DetectConflicts(List<InstalledModInfo> mods)
        {
            var conflicts = new List<ConflictInfo>();

            // Check for duplicate functionalities
            conflicts.AddRange(DetectDuplicateFunctionality(mods));

            // Check for known conflicts
            conflicts.AddRange(DetectKnownConflicts(mods));

            // Check for API version mismatches
            conflicts.AddRange(DetectApiMismatches(mods));

            return conflicts;
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
                        Severity = ConflictSeverity.Warning
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
                    Severity = ConflictSeverity.Warning
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
                                Severity = ConflictSeverity.Warning
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
                                Severity = ConflictSeverity.Critical
                            });
                        }
                    }
                }
            }

            return conflicts;
        }

        private List<ConflictInfo> DetectApiMismatches(List<InstalledModInfo> mods)
        {
            var conflicts = new List<ConflictInfo>();
            // For now, just check if mods are very outdated compared to others
            var newestVersion = mods.Max(m => m.LatestVersion ?? "");
            var oldMods = mods.Where(m => VersionComparer.NeedsUpdate(m.Version, newestVersion) && m.Version != "Unknown").ToList();

            if (oldMods.Count > 1)
            {
                for (int i = 0; i < oldMods.Count - 1; i++)
                {
                    conflicts.Add(new ConflictInfo
                    {
                        ModA = oldMods[i].Name,
                        ModB = oldMods[i + 1].Name,
                        Type = ConflictType.ApiVersionMismatch,
                        Description = $"Potential API version mismatch - both are outdated",
                        Severity = ConflictSeverity.Info
                    });
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
            // Add known conflicting mod pairs
            // These are examples and would be expanded in practice
            _knownConflicts.Add("CardSizeReduce", new List<string> { "UIScale" });
        }

        private void InitializeFunctionalities()
        {
            // Map mod name patterns to functionalities
            _functionalities.Add(("autosort", "inventory management"));
            _functionalities.Add(("smartinventory", "inventory management"));
            _functionalities.Add(("betterinventory", "inventory management"));
            _functionalities.Add(("copper", "metalworking"));
            _functionalities.Add(("metal", "metalworking"));
            _functionalities.Add(("herbs", "herbalism"));
            _functionalities.Add(("fungi", "herbalism"));
            _functionalities.Add(("water", "water mechanics"));
            _functionalities.Add(("irrigation", "water mechanics"));
        }
    }
}
