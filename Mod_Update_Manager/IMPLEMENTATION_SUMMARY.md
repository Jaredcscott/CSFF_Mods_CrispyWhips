# Mod Update Manager - Implementation Summary

Date: 2026-10-29
Version: 2.1.75

## Active Runtime Flow

1. `Plugin.Awake()` points `MumLog` at its BepInEx logger, then creates config, mapping, Nexus API, discovery, update-checker, scheduler, and UI services; also calls `SuiteVersionReader.RefreshAll()` to populate the Install & Update tab's installed-vs-bundled version comparison (before any scan, so no row can read as installed elsewhere yet).
2. `GameLoadPatch` notifies `Plugin.OnGameDataLoaded()` after game data loads.
3. `UpdateChecker.ScanMods()` scans BepInEx plugins for installed mods, and `SuiteVersionReader.RefreshAll(scan)` classifies the Install & Update rows again with that scan (the Refresh Mod List button does the same), which is what shows a suite mod installed under another folder.
4. If configured and an API key exists, `UpdateChecker.CheckAllMods()` queries Nexus for mapped mods.
5. `UpdateManagerUI` shows scan/check results, the suite installer, and settings in an IMGUI window toggled by F3 (default "Install & Update" tab).

## Key Components

- `ModScanner`: detects installed mods from plugin folders, one nested folder level, and loose DLLs.
- `ModMappingManager`: persists local mod name to Nexus ID mappings; on load drops rows that only repeat `KnownModRegistry` (a group of rows for one mod is dropped only when every row in it matches), rewriting the file only when it dropped something.
- `KnownModRegistry`: provides built-in fallback mappings for known CSFF mods.
- `NexusApiClient`: performs Nexus API calls and optional 24-hour response caching; retries an HTTP 429 once after 5 real-time seconds before reporting the mod rate-limited.
- `NexusModDiscovery`: optional, throttled ID scan for discovering mappings. Disabled by default.
- `UpdateChecker`: coordinates scans, version comparisons, and update-check completion events.
- `UpdateScheduler`: runs optional periodic update checks.
- `UpdateManagerUI`: IMGUI dashboard.
- `ConflictDetector`: conflict hints in two tiers, cached per mod scan: GUID-confirmed (two plugin DLLs register the same `[BepInPlugin]` GUID, read by `PluginMetadataReader` and paired by `PluginGuidCollisions`) and name-heuristic (name patterns and a hand-listed pair, labelled as such in the tab). Logs one summary line per scan.
- `CompatibilityChip`: reads the "Compatible with ... EA x.y" build from a fetched Nexus changelog and rates it against the running game version for the inline "Tested: EA x.y" line.
- `ModComparisonView`: simple update statistics from the current checked mod list.
- `SuiteModRegistry`: registry of the 15 crispywhips mods bundled in the Install & Update tab (folder name, display name, category, a hand-typed fallback description for the details panel; the embedded-resource key derives from the folder name) - grown from 9 at this doc's original writing by adding the pre-release mods (Partner Overhaul, Harsh Wilds, Far Territory, Forest Lore, Sirus23 Mod Collection, Utility Belt).
- `VersionLedger`: records each mod's installed-version changes (and first sight) to one small text file, read back at launch to drive the NEW chips and each row's History list (up to 20 entries, startup scan or suite Apply).
- `MumHandoff`: the upgrade note an older Mod Update Manager leaves for a newer one (`BepInEx/config/ModUpdateManager_Handoff.txt`) naming which suite mods Nexus had newer versions of; the newer MUM reads and deletes it on its first start and ticks the suite mods it updates on Install & Update.
- `SuiteKeepPolicy` / `SuiteInstaller.Kept.cs`: the opt-in `KeepPreviousSuiteVersion` path - moves the copy Apply replaces to `BepInEx/mum_work/kept/<Folder>/<version>/` instead of deleting it, prunes down to `KeptVersions` (1-3, default 1), and drives the **Roll back** button.
- `ModSuiteExtractor`: the in-game half of the suite installer - resolves `BepInEx/plugins/`, the work folder `BepInEx/mum_work/` beside it and the embedded ZIP, then runs `SuiteInstaller.InstallSteps` over them a step per `MoveNext` (`ExtractSteps`), which `UpdateManagerUI.ApplySuiteCoroutine` drains with one frame (`yield return null`) between steps.
- `SuiteInstaller`: checks the whole archive, extracts it into `BepInEx/mum_work/staging/<Folder>`, verifies every file's size there, then renames the installed folder to `BepInEx/mum_work/old/<Folder>` and the staging folder into its place, carries `SpriteCache/` across by rename, and deletes the old copy (falling back to `<Folder>__mum_staging` and `<Folder>__mum_old` inside `plugins/` when the work folder cannot be renamed into it). `InstallSteps` does that in bounded steps (`FilesPerStep`, `BytesPerStep`, `EntriesPerCheckStep`, `FilesPerDeleteStep`, tunables from the W0-3 research record), the two renames inside one step; `Install` drains it in one call. Any failure before the renames leaves the installed folder untouched; a failed second rename is undone. Host-free, and driven by `Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1`.
- `MumLog`: the log sink the host-free classes use, pointed at BepInEx's logger by `Plugin.Awake`.
- `SuiteVersionReader`: reads `ModInfo.json` once from each embedded ZIP (Name, Version, Description and the text itself, `ReadEmbeddedInfo`) and from the installed plugin folder, and classifies each suite entry as Up to Date / Update Available / Not Installed / Installed elsewhere / Unknown through `SuiteInstalledElsewhere.Classify`.
- `SuiteDescription`: host-free; resolves the details panel's text from the embedded `ModInfo.json` Description (decoded through `SimpleJson` and `JsonText`), falling back to the registry's typed line, trimmed and cut at `DetailsMaxChars` with a pointer to the Nexus page.
- `SuiteInstalledElsewhere`: host-free; `Find` matches the embedded `ModInfo.json` Name against a scan of `plugins/` for a copy outside the row's own folder and outside Mod Update Manager's own staging, backup and work folders; `Classify` decides a row's status, with a copy in the row's own folder winning over one elsewhere. `SuiteBatch` skips such a row and its two Select helpers leave it out.
- `MiniZip`: minimal Mono-compatible ZIP reader (`System.IO.Compression` is unavailable in the game's runtime); suite ZIPs are packed uncompressed (Stored) so this only needs to parse structure and copy bytes.

## Current Safety Boundaries

- The mod never downloads mods over the network, deletes arbitrary player data, or updates/restores mods without an explicit "Apply Updates" click. The Install & Update tab writes only to `BepInEx/plugins/<bundled-mod-folder>/` and its working folders `BepInEx/mum_work/staging/<bundled-mod-folder>` and `BepInEx/mum_work/old/<bundled-mod-folder>` (or, when the work folder cannot be used, the siblings `<bundled-mod-folder>__mum_staging` and `<bundled-mod-folder>__mum_old` inside `plugins/`), all removed once an apply finishes, from ZIPs embedded in the MUM DLL itself - no internet connection involved.
- Changelog viewing is on-demand per mod (never fetched at startup). The only thing read out of release notes is the "Compatible with ... EA x.y" build for the "Tested:" line; MUM does not validate compatibility itself, so beta compatibility must still be stated in published mod release notes.
- Nexus discovery is opt-in and throttled to one request every 90 seconds when enabled.
- Cache reads/writes respect `CachingEnabled`.
- Routine startup/status logging is kept mostly at Debug level; per-entry suite version-read failures log at Warning so the README's troubleshooting step for the Install & Update tab is actually visible in `LogOutput.log`.

## Future Work

See `Documentation/Design/Mod_Update_Manager_Round2_As_Built.md` (the mod's remaining feature scope, with ruled-out ideas such as self-update staging in its Considered table) and `ROADMAP.md`. The one-click suite installer itself shipped in v2.1.5 (2026-07-12) and is documented above, not in Future Work.
