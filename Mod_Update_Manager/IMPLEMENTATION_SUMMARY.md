# Mod Update Manager - Implementation Summary

Date: 2026-08-04
Version: 2.1.13

## Active Runtime Flow

1. `Plugin.Awake()` creates config, mapping, Nexus API, discovery, update-checker, scheduler, and UI services; also calls `SuiteVersionReader.RefreshAll()` to populate the Install & Update tab's installed-vs-bundled version comparison.
2. `GameLoadPatch` notifies `Plugin.OnGameDataLoaded()` after game data loads.
3. `UpdateChecker.ScanMods()` scans BepInEx plugins for installed mods.
4. If configured and an API key exists, `UpdateChecker.CheckAllMods()` queries Nexus for mapped mods.
5. `UpdateManagerUI` shows scan/check results, the suite installer, and settings in an IMGUI window toggled by F3 (default "Install & Update" tab).

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
- `SuiteModRegistry`: registry of the 8 crispywhips mods bundled in the Install & Update tab (folder name, display name, embedded-resource key, Nexus ID).
- `ModSuiteExtractor`: extraction engine for the suite installer — wipes the target plugin folder (preserving `SpriteCache/`), then extracts the embedded ZIP with path-traversal guards.
- `SuiteVersionReader`: reads `ModInfo.json` from each embedded ZIP and from the installed plugin folder to classify each suite entry as Up to Date / Update Available / Not Installed / Unknown.
- `MiniZip`: minimal Mono-compatible ZIP reader (`System.IO.Compression` is unavailable in the game's runtime); suite ZIPs are packed uncompressed (Stored) so this only needs to parse structure and copy bytes.

## Current Safety Boundaries

- The mod never downloads mods over the network, deletes arbitrary player data, or updates/restores mods without an explicit "Apply Updates" click. The Install & Update tab writes only to `BepInEx/plugins/<bundled-mod-folder>/` from ZIPs embedded in the MUM DLL itself — no internet connection involved.
- Changelog viewing is on-demand per mod (never fetched at startup); the dashboard does not interpret release notes — beta compatibility must be stated in published mod release notes.
- Nexus discovery is opt-in and throttled to one request every 90 seconds when enabled.
- Cache reads/writes respect `CachingEnabled`.
- Routine startup/status logging is kept mostly at Debug level; per-entry suite version-read failures log at Warning so the README's troubleshooting step for the Install & Update tab is actually visible in `LogOutput.log`.

## Future Work

See `Documentation/Ideas/Mod_Update_Manager/IDEAS.md` and `ROADMAP.md` for planned read-only mod profiles, self-update staging, suite health/compatibility surfacing, and richer analytics ideas. The one-click suite installer itself shipped in v2.1.5 (2026-07-12) and is documented above, not in Future Work.
