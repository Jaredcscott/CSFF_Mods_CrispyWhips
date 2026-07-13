# Mod Update Manager

**Version:** 2.1.10  
**Author:** Jared (crispywhips)  
**For:** Card Survival: Fantasy Forest (EA 0.65)

## Overview

Mod Update Manager is a non-intrusive BepInEx utility for checking installed CSFF mods against Nexus Mods. It scans local plugin folders, maps mods to Nexus IDs, compares versions, and shows results in an in-game dashboard.

It does not download, install, update, delete, or restore mods automatically.

It also does not validate game-beta compatibility by itself. When a mod update is published for a CSFF beta branch or newly released beta-compatible build, put the supported game build in the Nexus release notes so players can confirm compatibility after this tool reports the available version.

## Shipped Features

- **Crispywhips Mod Suite installer** — the "Install & Update" tab (the default tab on open) lets you install or update the whole crispywhips in-house mod family in one click: CSFF Mod Framework, Advanced Copper Tools, Herbs & Fungi, Water Driven Infrastructure, Community Mod Chest, Repeat Action, Quick Transfer, and Skill Speed Boost. Each of these mods ships bundled inside the Mod Update Manager DLL itself as an embedded ZIP — no separate download or manual copy is needed. Per-mod rows show installed vs. bundled version and a status badge (`[Up to Date]` / `[Update Available]` / `[Not Installed]`); "Select Out of Date / Not Installed" or "Select All" plus "Apply Updates" extracts the selected mods straight into `BepInEx/plugins/`, preserving the framework's `SpriteCache/`. A restart-required banner (with a one-click game-quit button) appears after applying. **Sirus23 Mod Collection is not part of this bundle** — install it separately. This does not touch Nexus and needs no API key.
- Scans installed mods on game startup and from the UI.
- Reads `ModInfo.json` from standard plugin folders and one nested folder level; also detects loose plugin DLLs without a `ModInfo.json` (listed with an Unknown version).
- Checks mapped mods against Nexus Mods when an API key is configured.
- Ships a built-in registry of known CSFF mods on Nexus (folder/display name → Nexus ID), so many published mods are recognized automatically with no manual setup.
- Supports manual mod-to-Nexus mappings through the Settings tab.
- Supports optional `NexusModId` entries in a mod's `ModInfo.json`.
- Provides tabs for all mods, updates available, up-to-date mods, unable-to-check mods, conflicts, analytics, and settings.
- Supports configurable startup checks and periodic background update checks.
- Caches Nexus API responses for 24 hours when caching is enabled.
- Includes optional, slow Nexus ID discovery for unmapped mods. This is disabled by default to avoid spending API quota.
- Shows lightweight conflict and analytics summaries based on local mod names and update status.
- **Favorites and Ignore** — Star mods to highlight them; ignore mods to exclude them from update checks. Both states are saved across sessions.
- **Per-mod Notes** — Attach a short note to any mod (e.g. testing status). Saved alongside favorite/ignore state.
- **Mod metadata** — Shows the Nexus summary and endorsement count for each checked mod.
- **Major-version warning** — Update entries where the major version changes are flagged in red.
- **Changelogs** — On-demand Nexus changelog fetch per mod, shown as an inline scrollable panel.
- **Mod list export** — Analytics tab generates a formatted plain-text list of all installed mods (name, version, status, notes) suitable for bug reports.
- **Self-exclusion** — A sentinel registry entry keeps Mod Update Manager from checking itself against Nexus and from being tagged with the generic "No Nexus Mod ID configured" error (it is not currently published on Nexus).

## Requirements

| Requirement | Notes |
|-------------|-------|
| Card Survival: Fantasy Forest | Steam version (EA 0.65) |
| BepInEx 5.x | Mod framework |
| Nexus Mods API Key | Free, requires Nexus account (only needed for the **My Mods** tab's Nexus checks — the **Install & Update** suite tab works fully offline) |
| Internet Connection | Needed to check for updates (not needed for the Install & Update suite tab, which extracts from mods bundled in its own DLL) |

**Dependencies:** none. Mod Update Manager has no runtime dependency on CSFFModFramework or any other mod, and no `[BepInDependency]` declarations — it is a fully standalone BepInEx plugin. It does, however, bundle copies of eight other in-house mods inside its own DLL for the Install & Update tab (see "Shipped Features"), and its built-in Nexus ID registry recognizes those and other CSFF mods by name for update tracking — neither of these makes MUM require them to run.

## Installation

1. Install BepInEx if not already installed.
2. Extract the `Mod_Update_Manager` folder to `BepInEx/plugins/`.
3. Launch the game.

## First-Time Setup

1. Launch the game.
2. Press **F3** to open the Mod Update Manager window.
3. Open the **Settings** tab.
4. Enter your Nexus Mods API key from `https://www.nexusmods.com/users/myaccount?tab=api+access`.
5. Click **Save**.

## Linking Mods to Nexus

Use one of these approaches:

- Add a manual mapping in the Settings tab.
- Enter a Nexus ID from the Unchecked tab.
- Add `"NexusModId": "123"` to a mod's `ModInfo.json`.

The Nexus ID is the number at the end of the mod page URL.

## UI Tabs

The window has four top-level tabs:

| Tab | Purpose |
|-----|---------|
| **Install & Update** | Default tab. The Crispywhips Mod Suite installer — see "Shipped Features" above. |
| **My Mods** | The Nexus-tracking dashboard, with its own sub-tab toolbar: **All** (every detected mod and its update status), **Updates Available** (mods with a newer Nexus version), **Up to Date** (checked mods already current), **Unmapped** (mods missing a Nexus ID or version to map) |
| **Conflicts** | Review lightweight name/functionality conflict hints |
| **Settings** | Configure API key, background checks, caching, and discovery; also shows the Analytics summary (update counts and simple estimates) on the same tab |

## Configuration

Config file: `BepInEx/config/crispywhips.mod_update_manager.cfg`

| Setting | Default | Description |
|---------|---------|-------------|
| NexusApiKey | empty | Your Nexus Mods API key |
| CheckOnStartup | true | Check for updates after game data loads |
| ShowOnlyUpdates | false | Filter the main list to update candidates |
| ToggleKey | F3 | Toggle the dashboard |
| WindowWidth | 900 | Width of the dashboard window |
| WindowHeight | 680 | Height of the dashboard window |
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
- Mod preferences (favorites, ignore, notes): `BepInEx/config/ModUpdateManager_Preferences.json`

## Release Notes And Beta Compatibility

Click the **Changelog** button on any checked mod to fetch and display its Nexus version history inline. The changelog view is on-demand and does not run at startup.

For beta-compatible mod releases, include the supported CSFF build in the published release notes, for example `Compatible with Card Survival: Fantasy Forest EA 0.65`. This keeps compatibility information available without implying that the manager performs automatic beta validation.

## Troubleshooting

**Window does not open with F3**
- Check `BepInEx/LogOutput.log` for errors.
- Verify BepInEx is installed correctly.
- Look for `Mod_Update_Manager v2.1.10 loaded.` in the log.

**Install & Update tab shows "Unknown" status for every mod**
- The embedded suite ZIPs failed to read (`SuiteVersionReader.RefreshAll` logs an error). Check `LogOutput.log`; this doesn't affect the Nexus-tracking tabs.

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
