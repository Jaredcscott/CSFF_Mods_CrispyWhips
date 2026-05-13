## Overview

**Version:** 1.7.4 — EA 0.63 compatibility

Skill Speed Boost provides comprehensive control over skill progression mechanics in Card Survival: Fantasy Forest. The mod enables natural staleness decay (like Fishing), customizable XP multipliers per skill, and per-location area familiarity bonuses.

### Key Features

1. **Staleness Management** — Skills decay naturally after 3 in-game hours (vs. vanilla permanent decay)
2. **Per-Skill XP Multipliers** — Set different learning rates for each skill (0-10x)
3. **Morning Study Bonus** — Optional XP multiplier during configurable morning hours (off by default)
4. **Area Familiarity** — XP bonus that grows the more you forage/work at a given location, capped per location and configurable

### Before vs After (Vanilla)

**Vanilla Behavior:**
- Craft 5 flint knives → XP drops from ~62% to ~2%
- Stop crafting → Staleness never decays (stuck at ~2% forever)
- Only workaround: craft something different

**With Skill Speed Boost (Default):**
- Craft 5 flint knives → XP drops from ~62% to ~2%
- Stop crafting for 3 in-game hours → Staleness decays, XP returns to ~62%
- Encourages variety while allowing focused grinding when desired

**With Custom Settings:**
- Set Smithing multiplier to 5x → Learn 5x faster
- Disable Tracking entirely by setting its multiplier to 0

## Installation

### Requirements
- [BepInEx 5.4.23.4+](https://github.com/BepInEx/BepInEx/releases)
- Card Survival: Fantasy Forest (EA 0.63)

### Steps
1. Download the latest release (v1.7.4+)
2. Copy the `Skill_Speed_Boost` folder into `BepInEx/plugins/`
3. Launch the game

Your plugins folder should look like:
```
BepInEx/
  plugins/
    Skill_Speed_Boost/
      Skill_Speed_Boost.dll
```

## Quick Start

### Default (Recommended)
The mod ships with these defaults:
- `SkillExpMultiplier = 1` (no XP boost)
- Staleness decay enabled (3 in-game hours)
- Per-skill customization enabled

Just install and play — no config needed.

### Common Configurations

**No Staleness (Relaxed)**
```ini
EnableSkillStaleness = false
SkillExpMultiplier = 3          ; 3x XP
```

**Vanilla+ (Slight Boost)**
```ini
EnableSkillStaleness = true
SkillExpMultiplier = 2          ; 2x XP
```

**Fast Leveling (Smithing Focus)**
```ini
SkillExpMultiplier = 2
Smithing_Multiplier = 5         ; 5x XP for smithing only
Archery_Multiplier = 2          ; 2x for other skills
Tracking_Multiplier = 0         ; Disabled
```

For detailed configuration, see **FEATURES.md**.

## Core Mechanics

### Staleness Decay (Base Feature)
In vanilla CSFF, staleness penalties are permanent. This mod enables natural decay:
- Skills decay staleness after **3 in-game hours** (12 game ticks) of non-use
- Mimics Fishing skill behavior (only vanilla skill with natural decay)
- Encourages skill variety without permanent penalties

### XP Multipliers
Control learning speed with global or per-skill multipliers:
- **Global multiplier:** Applies to all skills (1-10x)
- **Per-skill multipliers:** Override global for specific skills (0-10x each)
- Setting to `0` disables XP for that skill entirely
- Changes apply after reloading a save

## Affected Skills

The mod affects all 24+ skills with XP/staleness mechanics:

**Crafting:** Smithing, Tailoring, Carpentry, Cooking  
**Gathering:** Foraging, Herbal, Gathering  
**Combat:** Archery, Blade, Blunt, Dodge  
**Hunting:** Tracking, Trapping  
**Other:** Spellcraft, Axe, Mining, and more

See **FEATURES.md** for full per-skill configuration details.

## Technical Details

- **Load-time:** `GameLoad.LoadMainGameData` postfix configures staleness on all skill stats
- **Runtime:** `GameManager.ChangeStatValue` coroutine postfix applies XP multipliers (global, per-skill, morning bonus, area familiarity) on every skill XP gain
- **Area tracking:** `GameManager.ActionRoutine` coroutine postfix tracks current location for familiarity scoring
- **Staleness:** Sets `NoveltyCooldownDuration = 12` (12 ticks × 15 min = 3 hours)
- **Safe:** No permanent changes to save files; fully reversible

## Compatibility

- **Existing saves:** Safe to add/remove anytime
- **Other mods:** Compatible with all CSFF mods
- **Performance:** Minimal overhead — load-time scan of ~30 skill stats + lightweight coroutine postfix on XP gains
- **Save format:** No save file modifications

## Version History

### v1.7.3
- Fixed advertised dead code in docs (DEVELOPER_API.md, FEATURES.md)
- Added `GetAllSkillMultipliers()` to SkillConfigManager
- Dynamic skills discovered at load now auto-register per-skill config entries
- Startup log normalized to single Info line

### v1.7.2
- Runtime-hook rewrite: `SkillExpMultiplier` now applies via a `ChangeStatValue` postfix instead of a load-time ScriptableObject graph walk
- Drops ~17–25s of load-time tax to ~0s; multiple bonus sources (morning, area familiarity, global, per-skill) now compose in a single postfix

### v1.7.1
- Verified compatible with EA 0.62d (8-file delta from 0.62b — no API changes affecting this mod)

### v1.7.0
- **Area Familiarity** — Per-location XP bonus that scales with how often you work that tile
  - `AreaFamiliarityEnabled` (default true), `AreaFamiliarityMaxBonus` (default 0.30 = +30%)
  - `AreaFamiliarityVisitsForMaxBonus` (default 80) — XP-granting actions to reach the cap
  - Tracked per location UniqueID (e.g. all Ponds share one counter); persists in `BepInEx/config/SkillSpeedBoost/AreaFamiliarity.tsv`
  - Stacks multiplicatively with global, per-skill, and morning bonuses

### v1.6.1
- **Morning Study Bonus** — Optional XP multiplier during configurable morning hours
  - `MorningBonusEnabled` (default false), `MorningBonusMultiplier` (default 1.5x)
  - `MorningStartHour` / `MorningEndHour` in game-hours 0–23 (default 5–9)
  - Stacks with global and per-skill multipliers; applied as a real-time bonus on XP gains

### v2.0.0 / v1.5.x
- **Per-skill XP multipliers** — Set different learning rates for each skill
- **SkillConfigManager** — Per-skill configuration system
- Renamed to "Skill Speed Boost" (from "Remove Skill Staleness")

### v1.4.0
- Added no-staleness mode toggle
- Added 1-10x XP multiplier option
- Preserved default behavior

### v1.3.0
- Maintenance release
- Reduced logging noise

## Troubleshooting

**Issue:** "Changes didn't apply after editing config"
- **Fix:** Reload your save or restart the game. Hot-reload is disabled for stability.

**Issue:** "Per-skill settings not working"
- **Fix:** Ensure `EnablePerSkillMultipliers = true`. Check config spelling matches skill names exactly.

For more help, see **FEATURES.md** or check BepInEx LogOutput.log.

## Credits

Created by Jared (CrispyWhips)  
Framework: BepInEx, HarmonyLib
