## Overview

**Version:** 1.7.1 — EA 0.62d compatibility

Skill Speed Boost provides comprehensive control over skill progression mechanics in Card Survival: Fantasy Forest. The mod enables natural staleness decay (like Fishing), customizable XP multipliers per skill, and difficulty profiles.

### Key Features

1. **Staleness Management** — Skills decay naturally after 3 in-game hours (vs. vanilla permanent decay)
2. **Per-Skill XP Multipliers** — Set different learning rates for each skill (0-10x)
3. **Difficulty Profiles** — Switch between presets: VanillaPlus, Casual, Hardcore, Grinder, Balanced, Legacy
4. **Morning Study Bonus** — Optional XP multiplier during configurable morning hours (off by default)
5. **Area Familiarity** — XP bonus that grows the more you forage/work at a given location, capped per location and configurable

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
- Use Casual profile → 3x XP, no staleness penalties
- Disable Tracking entirely by setting its multiplier to 0

## Installation

### Requirements
- [BepInEx 5.4.23.4+](https://github.com/BepInEx/BepInEx/releases)
- Card Survival: Fantasy Forest (EA 0.62d)

### Steps
1. Download the latest release (v1.7.1+)
2. Copy the `SkillSpeedBoost` folder into `BepInEx/plugins/`
3. Launch the game

Your plugins folder should look like:
```
BepInEx/
  plugins/
    SkillSpeedBoost/
      Skill_Speed_Boost.dll
```

## Quick Start

### Default (Recommended)
The mod ships with these defaults:
- `ActiveProfile = Balanced` (2x XP, manageable staleness)
- Staleness decay enabled (3 in-game hours)
- Per-skill customization enabled

Just install and play — no config needed.

### Common Configurations

**Casual Mode (No Grind)**
```ini
ActiveProfile = Casual          ; 3x XP, no staleness
EnableSkillStaleness = false
```

**Hardcore Mode (Vanilla+)**
```ini
ActiveProfile = Hardcore        ; Vanilla difficulty
EnableSkillStaleness = true
SkillExpMultiplier = 1
```

**Fast Leveling (Smithing Focus)**
```ini
ActiveProfile = Balanced
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
- Customizable per-skill with `_StalenessMultiplier` settings

### XP Multipliers
Control learning speed with global or per-skill multipliers:
- **Global multiplier:** Applies to all skills (1-10x)
- **Per-skill multipliers:** Override global for specific skills (0-10x each)
- Setting to `0` disables XP for that skill entirely
- Changes apply after reloading a save

### Difficulty Profiles
Pre-configured settings for different playstyles:
- **VanillaPlus** — 2x XP + staleness (slight boost)
- **Casual** — 3x XP, no staleness (relaxed)
- **Hardcore** — 1x XP + staleness (vanilla)
- **Grinder** — 10x XP, no staleness (testing)
- **Balanced** — 2x XP + staleness (recommended)
- **Legacy** — 1x XP + staleness (original)

## Affected Skills

The mod affects all 24+ skills with XP/staleness mechanics:

**Crafting:** Smithing, Tailoring, Carpentry, Cooking  
**Gathering:** Foraging, Herbal, Gathering  
**Combat:** Archery, Blade, Blunt, Dodge  
**Hunting:** Tracking, Trapping  
**Other:** Spellcraft, Axe, Mining, and more

See **FEATURES.md** for full per-skill configuration details.

## Technical Details

- **Method:** Harmony runtime patching on `GameLoad.LoadMainGameData`
- **Staleness:** Sets `NoveltyCooldownDuration = 12` (12 ticks × 15 min = 3 hours)
- **XP Scaling:** Applies per-skill multipliers to all stat modifier types
- **Safe:** No permanent changes to save files; fully reversible

## Compatibility

- **Existing saves:** Safe to add/remove anytime
- **Other mods:** Compatible with all CSFF mods
- **Performance:** Minimal overhead (load-time patches only, no runtime cost)
- **Save format:** No save file modifications

## Version History

### v1.7.1 (Latest)
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

### v2.0.0
- **Per-skill XP multipliers** — Set different learning rates for each skill
- **Difficulty profiles** — VanillaPlus, Casual, Hardcore, Grinder, Balanced, Legacy
- **SkillConfigManager** — Intelligent per-skill configuration system

### v1.5.0
- Renamed to "Skill Speed Boost" (from "Remove Skill Staleness")
- Unified naming conventions

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
Framework: BepInEx, HarmonyLib, CSFFModFramework
