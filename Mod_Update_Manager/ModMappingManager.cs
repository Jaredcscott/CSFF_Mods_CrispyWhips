using System;
using System.IO;
using System.Collections.Generic;

// Host-free (no BepInEx, no UnityEngine; logging goes through MumLog) so
// Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 can compile it and drive LoadMappings.
namespace mod_update_manager
{
    /// <summary>
    /// Represents a mapping between a local mod and its Nexus Mods ID
    /// </summary>
    public class ModMapping
    {
        public string LocalModName { get; set; }
        public string NexusModId { get; set; }
        public string NexusModName { get; set; }
        public DateTime? LastChecked { get; set; }
    }

    /// <summary>
    /// Manages the mapping between local mod names and Nexus Mods IDs
    /// </summary>
    public class ModMappingManager
    {
        private Dictionary<string, ModMapping> _mappings;
        private string _mappingsPath;

        public ModMappingManager(string mappingsPath)
        {
            _mappingsPath = mappingsPath;
            _mappings = new Dictionary<string, ModMapping>(StringComparer.OrdinalIgnoreCase);
            LoadMappings();
        }

        /// <summary>
        /// Loads mappings from the JSON file
        /// </summary>
        public void LoadMappings()
        {
            try
            {
                if (File.Exists(_mappingsPath))
                {
                    var json = File.ReadAllText(_mappingsPath);
                    var mappingsList = SimpleJson.DeserializeMappings(json);
                    
                    _mappings.Clear();
                    if (mappingsList != null)
                    {
                        foreach (var mapping in mappingsList)
                        {
                            if (!string.IsNullOrEmpty(mapping.LocalModName))
                            {
                                _mappings[mapping.LocalModName] = mapping;
                            }
                        }
                    }

                    RetireRegistryDuplicates();
                }
                else
                {
                    // Create default mappings file with example
                    CreateDefaultMappings();
                }
            }
            catch (Exception ex)
            {
                MumLog.Error($"Failed to load mod mappings: {ex}");
            }
        }

        /// <summary>
        /// Drops rows that only repeat what KnownModRegistry already says. Through 2.1.58 every
        /// successful update check copied the mod's Nexus ID into this file, and a row here outranks
        /// the registry (UpdateChecker.GetNexusModId, UpdateManagerUI.GetNexusIdForMod), so each such
        /// row froze that mod's ID against any later registry correction.
        ///
        /// Never touches a row whose value differs from the registry (the player's own override), a
        /// row for a mod the registry does not know, or the "self" sentinel. Rows are grouped by the
        /// registry ID their key resolves to, and a group is dropped only when EVERY row in it equals
        /// the registry: with {"Herbs And Fungi": "22", "Herbs_And_Fungi": "999"} dropping the first
        /// would hand the mod to the second row, so both stay and no lookup changes.
        ///
        /// What this cannot see: with the opt-in slow Nexus discovery on, a discovered ID sits between
        /// this file and the registry in that lookup order, so a dropped row lets a DIFFERING
        /// discovered ID through. Discovery is off by default.
        ///
        /// Rewrites the file only when something was dropped, so an ordinary load changes nothing on
        /// disk.
        /// </summary>
        private void RetireRegistryDuplicates()
        {
            var groups = new Dictionary<string, List<ModMapping>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in _mappings.Values)
            {
                var known = KnownModRegistry.GetNexusModId(mapping.LocalModName);
                if (known == null || known == "self") continue;
                if (!groups.TryGetValue(known, out var rows))
                    groups[known] = rows = new List<ModMapping>();
                rows.Add(mapping);
            }

            var retired = new List<string>();
            foreach (var group in groups)
            {
                bool allDuplicate = true;
                foreach (var row in group.Value)
                {
                    if (!string.Equals((row.NexusModId ?? "").Trim(), group.Key.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        allDuplicate = false;
                        break;
                    }
                }
                if (!allDuplicate) continue;
                foreach (var row in group.Value)
                    retired.Add(row.LocalModName);
            }

            if (retired.Count == 0) return;
            foreach (var key in retired)
                _mappings.Remove(key);
            SaveMappings();
            MumLog.Info($"Dropped {retired.Count} mapping row(s) that duplicated the built-in registry: {string.Join(", ", retired)}.");
        }

        /// <summary>
        /// Saves mappings to the JSON file
        /// </summary>
        public void SaveMappings()
        {
            try
            {
                var mappingsList = new List<ModMapping>(_mappings.Values);
                var json = SimpleJson.SerializeMappings(mappingsList);
                File.WriteAllText(_mappingsPath, json);
                MumLog.Debug($"Saved {_mappings.Count} mod mappings");
            }
            catch (Exception ex)
            {
                MumLog.Error($"Failed to save mod mappings: {ex}");
            }
        }

        /// <summary>
        /// Creates a default mappings file with instructions
        /// </summary>
        private void CreateDefaultMappings()
        {
            // Start with an empty list - the known mod registry handles auto-detection, and the
            // only rows written here are the ones the user adds in the UI (UpdateChecker no longer
            // auto-saves checked IDs, which would outrank later registry corrections).
            var defaultMappings = new List<ModMapping>();

            try
            {
                var json = SimpleJson.SerializeMappings(defaultMappings);
                File.WriteAllText(_mappingsPath, json);
                MumLog.Debug($"Created default mod mappings file at: {_mappingsPath}");
            }
            catch (Exception ex)
            {
                MumLog.Error($"Failed to create default mappings: {ex}");
            }
        }

        /// <summary>
        /// Gets the Nexus Mod ID for a local mod
        /// </summary>
        public string GetNexusModId(string localModName)
        {
            if (_mappings.TryGetValue(localModName, out var mapping))
            {
                return mapping.NexusModId;
            }
            return null;
        }

        /// <summary>
        /// Gets the mapping for a local mod
        /// </summary>
        public ModMapping GetMapping(string localModName)
        {
            _mappings.TryGetValue(localModName, out var mapping);
            return mapping;
        }

        /// <summary>
        /// Sets or updates a mapping
        /// </summary>
        public void SetMapping(string localModName, string nexusModId, string nexusModName = null)
        {
            if (_mappings.TryGetValue(localModName, out var existing))
            {
                existing.NexusModId = nexusModId;
                existing.NexusModName = nexusModName ?? existing.NexusModName;
                existing.LastChecked = DateTime.Now;
            }
            else
            {
                _mappings[localModName] = new ModMapping
                {
                    LocalModName = localModName,
                    NexusModId = nexusModId,
                    NexusModName = nexusModName,
                    LastChecked = DateTime.Now
                };
            }
            
            SaveMappings();
        }

        /// <summary>
        /// Removes a mapping
        /// </summary>
        public void RemoveMapping(string localModName)
        {
            if (_mappings.Remove(localModName))
            {
                SaveMappings();
            }
        }

        /// <summary>
        /// Gets all mappings
        /// </summary>
        public IEnumerable<ModMapping> GetAllMappings()
        {
            return _mappings.Values;
        }
    }
}
