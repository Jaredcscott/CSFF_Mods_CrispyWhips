# Mod Update Manager

**Version:** 2.0.3  
**Author:** Jared (crispywhips)  
**For:** Card Survival: Fantasy Forest (EA 0.63)

## Overview

Mod Update Manager is a non-intrusive BepInEx utility for checking installed CSFF mods against Nexus Mods. It scans local plugin folders, maps mods to Nexus IDs, compares versions, and shows results in an in-game dashboard.

It does not download, install, update, delete, or restore mods automatically.

It also does not validate game-beta compatibility by itself. When a mod update is published for a CSFF beta branch or newly released beta-compatible build, put the supported game build in the Nexus release notes so players can confirm compatibility after this tool reports the available version.

## Shipped Features

- Scans installed mods on game startup and from the UI.
- Reads `ModInfo.json` from standard plugin folders and one nested folder level.
- Checks mapped mods against Nexus Mods when an API key is configured.
- Supports manual mod-to-Nexus mappings through the Settings tab.
- Supports optional `NexusModId` entries in a mod's `ModInfo.json`.
- Provides tabs for all mods, updates available, up-to-date mods, unable-to-check mods, conflicts, analytics, and settings.
- Supports configurable startup checks and periodic background update checks.
- Caches Nexus API responses for 24 hours when caching is enabled.
- Includes optional, slow Nexus ID discovery for unmapped mods. This is disabled by default to avoid spending API quota.
- Shows lightweight conflict and analytics summaries based on local mod names and update status.

## Requirements

| Requirement | Notes |
|-------------|-------|
| Card Survival: Fantasy Forest | Steam version (EA 0.63) |
| BepInEx 5.x | Mod framework |
| Nexus Mods API Key | Free, requires Nexus account |
| Internet Connection | Needed to check for updates |

## Installation

1. Install BepInEx if not already installed.
2. Extract the `Mod_Update_Manager` folder to `BepInEx/plugins/`.
3. Launch the game.

## First-Time Setup

1. Launch the game.
2. Press **F8** to open the Mod Update Manager window.
3. Open the **Settings** tab.
4. Enter your Nexus Mods API key from `https://www.nexusmods.com/users/myaccount?tab=api+access`.
5. Click **Save**.

## Linking Mods to Nexus

Use one of these approaches:

- Add a manual mapping in the Settings tab.
- Enter a Nexus ID from the Unable to Check tab.
- Add `"NexusModId": "123"` to a mod's `ModInfo.json`.

The Nexus ID is the number at the end of the mod page URL.

## UI Tabs

| Tab | Purpose |
|-----|---------|
| **All Mods** | View all detected mods and their update status |
| **Updates Available** | See only mods with newer Nexus versions |
| **Up to Date** | View checked mods already on the latest version |
| **Unable to Check** | Map mods missing a Nexus ID or version |
| **Conflicts** | Review lightweight name/functionality conflict hints |
| **Analytics** | View update counts and simple estimates |
| **Settings** | Configure API, background checks, caching, and discovery |

## Configuration

Config file: `BepInEx/config/crispywhips.Mod_Update_Manager.cfg`

| Setting | Default | Description |
|---------|---------|-------------|
| NexusApiKey | empty | Your Nexus Mods API key |
| CheckOnStartup | true | Check for updates after game data loads |
| ShowOnlyUpdates | false | Filter the main list to update candidates |
| ToggleKey | F8 | Toggle the dashboard |
| EnableBackgroundChecking | false | Periodically check mapped mods in the background |
| CheckIntervalMinutes | 60 | Minutes between background checks, 10-1440 |
| ShowConflictWarnings | true | Show conflict hints in the Conflicts tab |
| CachingEnabled | true | Cache Nexus responses for 24 hours |
| EnableNexusDiscovery | false | Slowly scan Nexus IDs to discover mappings |
| DiscoveryMaxScanId | 2000 | Maximum Nexus ID to scan when discovery is enabled |
| DiscoveryMaxConsecutiveMisses | 500 | Stop discovery after this many misses |

## Data Locations

- Mod mappings: `BepInEx/config/ModUpdateManager_Mappings.json`
- Nexus response cache: `BepInEx/config/ModUpdateManager_Cache.json`
- Nexus discovery cache: `BepInEx/config/nexus_discovery_cache.json`

## Release Notes And Beta Compatibility

Mod Update Manager currently compares installed and Nexus-reported version strings. It does not display Nexus changelogs or inspect release notes in the dashboard yet.

For beta-compatible mod releases, include the supported CSFF build in the published release notes, for example `Compatible with Card Survival: Fantasy Forest EA 0.63 beta`. This keeps compatibility information available without implying that the manager performs automatic beta validation.

## Troubleshooting

**Window does not open with F8**
- Check `BepInEx/LogOutput.log` for errors.
- Verify BepInEx is installed correctly.
- Look for `Mod_Update_Manager v2.0.3 loaded.` in the log.

**API key not set**
- Enter your Nexus API key in the Settings tab and click Save.

**Updates are not detected**
- Verify the Nexus Mod ID is correct.
- Verify the installed mod has a readable version in `ModInfo.json`.
- Check whether the response cache is serving a recent result; disable caching or wait for expiry if needed.

## Credits

- **Author:** Jared (crispywhips)
- **Framework:** BepInEx and Harmony
- **API:** Nexus Mods API
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games

## License

MIT License - feel free to modify and redistribute.
