# Mod Update Manager

**Version:** 2.0.0  
**Author:** Jared (crispywhips)  
**For:** Card Survival: Fantasy Forest (EA 0.62b)

---

## Overview

Mod Update Manager is a comprehensive utility mod for managing mod updates, versioning, backups, and analyzing mod ecosystem health. Check for updates from Nexus Mods, maintain version history, detect conflicts, and more.

## Features

### Core Features
- Scans installed mods on game startup
- Checks for updates against Nexus Mods releases
- In-game UI dashboard (press **F8** to toggle)
- Link local mods to Nexus Mod IDs for tracking
- Configurable auto-check and API key settings
- Non-intrusive — never downloads or installs anything automatically

### New in v2.0.0
- **Backup & Restore System** — Automatic backup before updates, restore from old versions
- **Version History** — Track all version changes, installation dates, and rollback history
- **Background Checking** — Periodic automatic update checks at configurable intervals
- **API Response Caching** — Cache Nexus API responses to reduce network requests (24-hour expiry)
- **Conflict Detection** — Detect potential mod conflicts based on functionality overlap
- **Update Analytics** — View update statistics, download size estimates, and trends
- **Ignore & Favorite Lists** — Mark mods to ignore or prioritize updates
- **Enhanced Settings Dashboard** — Comprehensive settings for backups, scheduling, and performance
- **New UI Tabs** — Dedicated tabs for conflicts, analytics, and advanced settings

## Requirements

| Requirement | Notes |
|-------------|-------|
| Card Survival: Fantasy Forest | Steam version (EA 0.62b) |
| BepInEx 5.x | Mod framework |
| Nexus Mods API Key | Free, requires Nexus account |
| Internet Connection | Needed to check for updates |

## Installation

1. Install BepInEx if not already installed
2. Extract the `Mod_Update_Manager` folder to `BepInEx/plugins/`
4. Launch game

### File Structure

```
BepInEx/plugins/Mod_Update_Manager/
├── Mod_Update_Manager.dll
└── ModInfo.json
```

## First-Time Setup

1. Launch the game
2. Press **F8** to open the Mod Update Manager window
3. Go to the **Settings** tab
4. Enter your Nexus Mods API Key:
   - Visit [Nexus Mods API Settings](https://www.nexusmods.com/users/myaccount?tab=api)
   - Copy your Personal API Key
   - Paste into the API Key field
5. Click **Save Settings**

## Usage

### Linking Mods to Nexus

1. Open the **Mod Mapping** tab (F8)
2. For each mod, enter its Nexus Mod ID (the number from the mod page URL)
3. Click **Save Mappings**

Alternative: Add `"NexusModId": "123"` to a mod's `ModInfo.json`

### Checking for Updates

1. Go to the **Updates** tab
2. Click **Check for Updates**
3. Results show: up-to-date, update available, or not linked

### UI Tabs

| Tab | Purpose |
|-----|---------|
| **All Mods** | View all installed mods and their update status |
| **Updates Available** | See only mods with new versions available |
| **Up to Date** | View mods running the latest version |
| **Unable to Check** | See mods without Nexus mapping or with version errors |
| **Conflicts** | Detect and review potential mod conflicts |
| **Analytics** | View update statistics, trends, and estimates |
| **Settings** | Configure API, backups, scheduling, and more |

### Controls

| Key | Action |
|-----|--------|
| **F8** | Toggle the Mod Update Manager window |

## Configuration

Config file: `BepInEx/config/crispywhips.Mod_Update_Manager.cfg`

### API Settings
| Setting | Default | Description |
|---------|---------|-------------|
| NexusApiKey | (empty) | Your Nexus Mods API key |

### Behavior Settings
| Setting | Default | Description |
|---------|---------|-------------|
| CheckOnStartup | true | Automatically check for updates when game loads |
| ShowOnlyUpdates | false | Only show mods with updates available |

### Backup Settings
| Setting | Default | Description |
|---------|---------|-------------|
| AutomaticBackups | true | Create backup before updating mods |
| MaxBackupsPerMod | 3 | Maximum backups to keep per mod |

### Scheduling Settings
| Setting | Default | Description |
|---------|---------|-------------|
| EnableBackgroundChecking | false | Periodically check for updates in background |
| CheckIntervalMinutes | 60 | Minutes between background checks (10-1440) |

### Analysis Settings
| Setting | Default | Description |
|---------|---------|-------------|
| ShowConflictWarnings | true | Alert when potential mod conflicts detected |

### Performance Settings
| Setting | Default | Description |
|---------|---------|-------------|
| CachingEnabled | true | Cache Nexus API responses |

### Data Locations
- Mod mappings: `BepInEx/config/ModUpdateManager_Mappings.json`
- Backups: `BepInEx/config/ModUpdateManager_Backups/`
- Version history: `BepInEx/config/ModUpdateManager_History/`
- Ignore/Favorite lists: `BepInEx/config/ModUpdateManager_Lists/`

## Advanced Features

### Backup & Restore
- Automatic backups are created before installing updates (if enabled)
- Backups include complete mod folder copies with version information
- Restore from any previous backup using the UI
- Configurable backup retention (default: 3 versions)

### Version History & Analytics
- Track every mod version installed and the date
- View update frequency analysis for each mod
- Identify actively maintained vs abandoned mods
- Calculate average time between updates

### Background Checking
- Enable periodic update checks without manual intervention
- Configurable check interval (10 minutes to 24 hours)
- Checks run automatically without notification unless updates found
- Useful for always staying current on critical updates

### API Caching
- Responses from Nexus API are cached for 24 hours
- Reduces network requests and improves performance
- Cache automatically expires and refreshes
- Useful for checking multiple times without rate limiting

### Conflict Detection
- Automatically identifies mods with overlapping functionality
- Analyzes mod names, tags, and known conflicts
- Provides severity ratings (Info, Warning, Critical)
- Suggests resolutions for detected conflicts

### Update Analytics Dashboard
- View summary of all installed mods
- See how many need updates and estimated download size
- Identify critical updates
- Track background check schedule

## Troubleshooting

**Window doesn't open (F8)**
- Check `BepInEx/LogOutput.log` for errors
- Verify BepInEx is installed correctly
- Look for "Mod Update Manager is loaded!" in log

**"API Key not set" error**
- Enter your Nexus API key in the Settings tab and click Save

**Updates not detected**
- Verify the Nexus Mod ID is correct (number from URL)
- Version formats must be comparable (e.g., "1.0.0" vs "1.0.1")

## Credits

- **Author:** Jared (crispywhips)
- **Framework:** BepInEx & Harmony
- **API:** Nexus Mods API
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games

## License

MIT License - Feel free to modify and redistribute.
