using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace mod_update_manager
{
    /// <summary>
    /// Manages ignored and favorite mods
    /// </summary>
    public class IgnoreFavoriteManager
    {
        private string _configPath;
        private HashSet<string> _ignoredMods;
        private HashSet<string> _favoriteMods;

        public event Action<string> OnModIgnored;
        public event Action<string> OnModUnignored;
        public event Action<string> OnModFavorited;
        public event Action<string> OnModUnfavorited;

        public IgnoreFavoriteManager(string configPath)
        {
            _configPath = configPath;
            _ignoredMods = new HashSet<string>();
            _favoriteMods = new HashSet<string>();
            LoadSettings();
        }

        /// <summary>
        /// Check if a mod is ignored
        /// </summary>
        public bool IsIgnored(string modName)
        {
            return _ignoredMods.Contains(NormalizeName(modName));
        }

        /// <summary>
        /// Check if a mod is favorite
        /// </summary>
        public bool IsFavorite(string modName)
        {
            return _favoriteMods.Contains(NormalizeName(modName));
        }

        /// <summary>
        /// Toggle ignore status
        /// </summary>
        public void ToggleIgnore(string modName)
        {
            var normalized = NormalizeName(modName);
            if (_ignoredMods.Contains(normalized))
            {
                _ignoredMods.Remove(normalized);
                OnModUnignored?.Invoke(modName);
                Plugin.Logger.LogInfo($"Mod {modName} is no longer ignored");
            }
            else
            {
                _ignoredMods.Add(normalized);
                OnModIgnored?.Invoke(modName);
                Plugin.Logger.LogInfo($"Mod {modName} is now ignored");
            }
            SaveSettings();
        }

        /// <summary>
        /// Toggle favorite status
        /// </summary>
        public void ToggleFavorite(string modName)
        {
            var normalized = NormalizeName(modName);
            if (_favoriteMods.Contains(normalized))
            {
                _favoriteMods.Remove(normalized);
                OnModUnfavorited?.Invoke(modName);
                Plugin.Logger.LogInfo($"Mod {modName} is no longer favorite");
            }
            else
            {
                _favoriteMods.Add(normalized);
                OnModFavorited?.Invoke(modName);
                Plugin.Logger.LogInfo($"Mod {modName} is now favorite");
            }
            SaveSettings();
        }

        /// <summary>
        /// Get all ignored mods
        /// </summary>
        public List<string> GetIgnoredMods()
        {
            return _ignoredMods.ToList();
        }

        /// <summary>
        /// Get all favorite mods
        /// </summary>
        public List<string> GetFavoriteMods()
        {
            return _favoriteMods.ToList();
        }

        /// <summary>
        /// Get count of ignored mods
        /// </summary>
        public int GetIgnoredCount()
        {
            return _ignoredMods.Count;
        }

        /// <summary>
        /// Get count of favorite mods
        /// </summary>
        public int GetFavoriteCount()
        {
            return _favoriteMods.Count;
        }

        /// <summary>
        /// Import ignore/favorite list from external file
        /// </summary>
        public bool ImportSettings(string importPath)
        {
            try
            {
                if (!File.Exists(importPath))
                    return false;

                var lines = File.ReadAllLines(importPath);
                _ignoredMods.Clear();
                _favoriteMods.Clear();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split('=');
                    if (parts.Length != 2)
                        continue;

                    var modName = NormalizeName(parts[0]);
                    var state = parts[1].ToLower();

                    if (state == "ignored")
                        _ignoredMods.Add(modName);
                    else if (state == "favorite")
                        _favoriteMods.Add(modName);
                }

                SaveSettings();
                Plugin.Logger.LogInfo($"Imported settings from {importPath}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to import settings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export ignore/favorite list to file
        /// </summary>
        public bool ExportSettings(string exportPath)
        {
            try
            {
                var lines = new List<string>();

                foreach (var mod in _ignoredMods)
                    lines.Add($"{mod}=ignored");

                foreach (var mod in _favoriteMods)
                    lines.Add($"{mod}=favorite");

                File.WriteAllLines(exportPath, lines);
                Plugin.Logger.LogInfo($"Exported settings to {exportPath}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to export settings: {ex.Message}");
                return false;
            }
        }

        private void LoadSettings()
        {
            try
            {
                var settingsFile = Path.Combine(_configPath, "ignore_favorite.json");
                if (!File.Exists(settingsFile))
                    return;

                var lines = File.ReadAllLines(settingsFile);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split('=');
                    if (parts.Length != 2)
                        continue;

                    var modName = parts[0];
                    var state = parts[1].ToLower();

                    if (state == "ignored")
                        _ignoredMods.Add(modName);
                    else if (state == "favorite")
                        _favoriteMods.Add(modName);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to load ignore/favorite settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(_configPath))
                    Directory.CreateDirectory(_configPath);

                var settingsFile = Path.Combine(_configPath, "ignore_favorite.json");
                var lines = new List<string>();

                foreach (var mod in _ignoredMods)
                    lines.Add($"{mod}=ignored");

                foreach (var mod in _favoriteMods)
                    lines.Add($"{mod}=favorite");

                File.WriteAllLines(settingsFile, lines);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to save ignore/favorite settings: {ex.Message}");
            }
        }

        private string NormalizeName(string name)
        {
            return name.ToLowerInvariant().Trim();
        }
    }
}
