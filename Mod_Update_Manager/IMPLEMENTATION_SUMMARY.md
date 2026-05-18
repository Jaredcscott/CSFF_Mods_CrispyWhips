# Mod Update Manager - Implementation Summary

Date: 2026-04-30
Version: 2.0.4

## Active Runtime Flow

1. `Plugin.Awake()` creates config, mapping, Nexus API, discovery, update-checker, scheduler, and UI services.
2. `GameLoadPatch` notifies `Plugin.OnGameDataLoaded()` after game data loads.
3. `UpdateChecker.ScanMods()` scans BepInEx plugins for installed mods.
4. If configured and an API key exists, `UpdateChecker.CheckAllMods()` queries Nexus for mapped mods.
5. `UpdateManagerUI` shows scan/check results and settings in an IMGUI window toggled by F8.

## Key Components

- `ModScanner`: detects installed mods from plugin folders, one nested folder level, and loose DLLs.
- `ModMappingManager`: persists local mod name to Nexus ID mappings.
- `KnownModRegistry`: provides built-in fallback mappings for known CSFF mods.
- `NexusApiClient`: performs Nexus API calls and optional 24-hour response caching.
- `NexusModDiscovery`: optional, throttled ID scan for discovering mappings. Disabled by default.
- `UpdateChecker`: coordinates scans, version comparisons, and update-check completion events.
- `UpdateScheduler`: runs optional periodic update checks.
- `UpdateManagerUI`: IMGUI dashboard.
- `ConflictDetector`: lightweight local conflict hints.
- `ModComparisonView`: simple update statistics from the current checked mod list.

## Current Safety Boundaries

- The mod never downloads, installs, deletes, updates, or restores player mods automatically.
- The dashboard does not parse or display release notes; beta compatibility must be stated in published mod release notes.
- Nexus discovery is opt-in and throttled to one request every 90 seconds when enabled.
- Cache reads/writes respect `CachingEnabled`.
- Routine startup/status logging is kept mostly at Debug level.

## Future Work

See `Documentation/Ideas/Mod_Update_Manager/FUTURE_FEATURES.md` for planned backup, rollback, changelog, ignore/favorite, and richer analytics ideas.
