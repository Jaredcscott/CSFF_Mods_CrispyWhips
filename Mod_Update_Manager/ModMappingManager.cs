using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;

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
                }
                else
                {
                    // Create default mappings file with example
                    CreateDefaultMappings();
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to load mod mappings: {ex.Message}");
            }
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
                Plugin.Logger.LogDebug($"Saved {_mappings.Count} mod mappings");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to save mod mappings: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a default mappings file with instructions
        /// </summary>
        private void CreateDefaultMappings()
        {
            // Start with an empty list — the known mod registry handles auto-detection,
            // and user mappings will be added via the UI or auto-saved after first check.
            var defaultMappings = new List<ModMapping>();

            try
            {
                var json = SimpleJson.SerializeMappings(defaultMappings);
                
                // Add comment header
                var header = @"// Mod Update Manager - Mod Mappings
// This file maps your local mod names to their Nexus Mods IDs
// 
// To add a mapping:
// 1. Find your mod on Nexus Mods (e.g., https://www.nexusmods.com/cardsurvivalfantasyforest/mods/123)
// 2. The number at the end of the URL is the NexusModId (123 in this example)
// 3. Add an entry below with your local mod's folder name and the Nexus ID
//
// You can also add ""NexusModId"": ""123"" directly to a mod's ModInfo.json file
//
";
                File.WriteAllText(_mappingsPath, header + "\n" + json);
                Plugin.Logger.LogDebug($"Created default mod mappings file at: {_mappingsPath}");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to create default mappings: {ex.Message}");
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
