# Skill Speed Boost

**Version:** 1.10.4
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.67h)

---

## Overview

Skill Speed Boost provides comprehensive control over skill progression mechanics in Card Survival: Fantasy Forest. The mod enables natural staleness decay (like Fishing), customizable XP multipliers per skill, per-location area familiarity bonuses, difficulty presets, and a combo synergy system for chaining related skills.

### Key Features

1. **Staleness Management** — Skills decay naturally after 3 in-game hours; adjustable per-skill with individual rate multipliers
2. **Per-Skill XP Multipliers** — Set different learning rates for each skill (0-10x)
3. **Morning Study Bonus** — Optional XP multiplier during configurable morning hours (off by default)
4. **Area Familiarity** — XP bonus that grows the more you forage/work at a given location, capped per location and configurable
5. **Difficulty Profiles** - Named presets (Balanced, Casual, Hardcore, Grinder, Immersive, etc.) that set the whole stack with one config key: ExpMultiplier, Staleness, and all seven feature toggles
6. **Skill Synergies** — Combo XP bonus for chaining related skills in sequence: +10% per consecutive related action, capped at +50% (5-action combo), resets after 5 minutes of inactivity (off by default)
7. **Level Scaling** - Optional XP bonus that grows as a skill approaches its maximum level, compensating for the increasing XP cost at higher levels, with a choice of Linear, EaseIn or EaseOut ramp (off by default)
8. **Daily First-Use Bonus** - Optional multiplier on each skill's first use of the in-game day, rewarding a broad day over a single grind (off by default)
9. **Well-Rested Bonus** - Optional multiplier while Energy, Satiation and Hydration are all above a threshold (off by default)
10. **Low-Condition XP Penalty** - Optional sub-1.0 multiplier while Energy, Satiation or Hydration is below a threshold: the inverse of the well-rested bonus, and the only setting in the mod that makes XP move slower (off by default)
11. **Composed-Multiplier Cap** - Optional ceiling on the combined total after every bonus has stacked, so overlapping bonuses cannot silently reach a figure no single slider suggests (uncapped by default)
12. **Effective-Settings Log** - Optional one-line-per-skill dump at load showing the settings that actually resolved, for when a setting does not seem to be taking effect (off by default)

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
- **CSFFModFramework** (recommended) — since v1.9.2, `GameLoadPatch`, `AreaFamiliarityPatch`, and `MorningBonusPatch` (the staleness config, area familiarity, and morning bonus features) call the framework's `Api.Reflect`/`Api.StatAccess`/`Api.CardUtil` utilities directly. The mod declares `CSFFModFramework` as a `SoftDependency` for load order; without it installed, those features throw instead of applying.
- Card Survival: Fantasy Forest (EA 0.67h)

### Steps
1. Install CSFFModFramework in `BepInEx/plugins/CSFF_Mod_Framework/`
2. Download the latest release (v1.10.1+)
3. Copy the `Skill_Speed_Boost` folder into `BepInEx/plugins/`
4. Launch the game

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

**Immersive (no raw multiplier)**
```ini
ActiveProfile = Immersive       ; 1x XP, staleness on, and only the
                                ; diegetic bonuses: area familiarity,
                                ; morning window, daily first use,
                                ; well-rested - plus the low-condition
                                ; penalty, so a neglected body learns
                                ; slower. Sets all of them for you.
```

**Stacking several bonuses safely**
```ini
SkillExpMultiplier = 3
MorningBonusEnabled = true
EnableSkillSynergies = true
MaxComposedMultiplier = 6       ; never award more than 6x, however
                                ; many bonuses line up at once
LogMultiplierCapHits = true     ; log each time the cap actually bites
```

**Working out why a setting is not taking effect**
```ini
LogEffectiveSettings = true     ; one line per skill in LogOutput.log
                                ; after load, showing what resolved
LogSkillXpGains = true          ; one line per XP gain, before and
                                ; after the bonuses
```

For detailed configuration, see **FEATURES.md**.

## Core Mechanics

### Staleness Decay (Base Feature)
In vanilla CSFF, most skills' staleness penalties never decay on their own — a handful of skills (Fishing among them) already decay, but at wildly inconsistent rates. This mod normalizes decay across every skill:
- Skills decay staleness after **3 in-game hours** (12 game ticks) of non-use by default
- Applies the same consistent, configurable decay rate to every skill — including the few skills vanilla already decays, just inconsistently
- Encourages skill variety without permanent penalties

### XP Multipliers
Control learning speed with global or per-skill multipliers:
- **Global multiplier:** Applies to all skills (1-10x)
- **Per-skill multipliers:** Override global for specific skills (0-10x each)
- Setting to `0` disables XP for that skill entirely
- Changes apply after reloading a save

## Affected Skills

Rather than a hardcoded list, the mod scans every vanilla `GameStat` at load time and treats any stat that uses the same staleness flag as Fishing (`UsesNovelty`) as a skill, auto-registering per-skill XP/staleness config for it under its real in-game name. Nine stats set that flag without being skills and are excluded by name - Stress, Morale, Profile, Altered Mindstate, Mental Structure, Focus, Gratification, Loneliness and Thought Depth - so no XP or staleness setting in this mod touches your mental stats. That leaves 32 vanilla skills, including:

**Crafting:** Smithing, Tailoring, Weaving, Cooking, Woodworking, Leatherworking, Pottery, Metalworking, Knapping, Crafting  
**Gathering:** Herbalism, Fishing, Spear Fishing  
**Combat:** Archery, Axe Fighting, Knife Fighting, ClubFighting, Spear Fighting, Rock Throwing, Sling, Handguns  
**Hunting:** Tracking, Trapping, Butchering  
**Other:** Stealth, Climbing, Swimming, Socials, Insight, Percussion, Wind Instruments, Perception

Config keys use these names EXACTLY as spelled above, spaces included: `Knife Fighting_Multiplier` has a space, `ClubFighting_Multiplier` does not. A key whose stem does not match a real skill name is simply never read - set `LogEffectiveSettings = true` to see what actually resolved.

Any skill added by another mod is picked up the same way automatically — no update to this mod is needed. See **FEATURES.md** for full per-skill configuration details.

## Technical Details

- **Load-time:** `GameLoad.LoadMainGameData` postfix configures staleness on all skill stats
- **Runtime:** `GameManager.ChangeStatValue` coroutine postfix applies XP multipliers (global, per-skill, morning bonus, area familiarity, synergies, level scaling, daily first use, well-rested and the low-condition penalty) on every skill XP gain. It is the single composition point: the composed total is applied in whichever direction it points, so a penalty reduces the gain rather than being discarded. Gains are measured on the skill's trained (base) value, so an effect that temporarily lowers a skill does not hide XP earned while it lasts
- **Area tracking:** `GameManager.ActionRoutine` coroutine postfix tracks current location for familiarity scoring
- **Staleness:** Sets `NoveltyCooldownDuration = 12` (12 ticks × 15 min = 3 hours)
- **Safe:** No permanent changes to save files; fully reversible

## Compatibility

- **Existing saves:** Safe to add/remove anytime
- **Other mods:** Compatible with all CSFF mods
- **Dependencies:** `CSFFModFramework` (soft — see Requirements above). No dependency on any content mod, and no other mod depends on Skill Speed Boost.
- **Performance:** Minimal overhead — load-time scan of ~30 skill stats, plus one coroutine postfix that runs on every stat change and does its work only on skill XP gains (it early-outs immediately when no bonus feature is enabled)
- **Save format:** No save file modifications

## Version History

### v1.10.4 (current)
- **Fixed: XP settings now apply while a status holds a skill down.** "Animals noticed your
  Actions" (noisy work such as chopping wood or shovelling snow) takes 75 off Stealth, which shows low
  Stealth as 0. XP earned meanwhile still reached the skill, but the mod read the lowered value, saw
  no gain and applied none of your XP settings. It now reads the trained value underneath, so every
  multiplier applies, a per-skill `0` stops that XP too, and level scaling uses your real level.
- **New: `LogSkillXpGains`** (default off) logs each skill XP gain before and after the bonuses.

### v1.10.3
- **Fixed: a campfire going out no longer raises your Stealth.** A lit campfire lowers Stealth by
  150 at camp. When it went out, the mod counted those points coming back as XP and added its bonus
  on top, so Stealth crept above what you had trained until you reloaded. The same was true of any
  temporary effect on a skill ending. Only real XP gains are multiplied now. Reported on GitHub
  after EA 0.66a; the 1.9.6 diagnostics never found it because the cause was in this mod.

### v1.10.2
- **New: an optional low-condition XP penalty.** `LowConditionPenaltyEnabled` (default off) with
  `LowConditionThreshold` (default 60) and `LowConditionMultiplier` (default 0.5) multiply skill XP
  by a sub-1.0 factor while Energy, Satiation OR Hydration is below the threshold percentage of its
  maximum. It is the exact inverse of the well-rested bonus at the same threshold, and the first
  setting in the mod that makes XP move slower. Both numbers are starting points, not balance
  verdicts - tune them to taste.
- **Changed: `Hardcore`, `Legacy` and `Immersive` now turn the penalty ON.** Those three presets
  previously had nothing to switch on, only bonuses to switch off. An existing config is NOT
  affected on upgrade: since 1.10.1 a preset is written once and your `AppliedProfile` already
  records it, so nothing is re-applied until you choose a preset again (or clear that key).
- **Changed: the composed multiplier is now applied in both directions.** The postfix used to stop
  as soon as the composed total was not above 1, which was correct while every term could only
  raise it. A sub-1.0 total is now written too, and only a total of exactly 1 is treated as no
  change. Nothing about the existing bonuses moves: none of them can produce a factor below 1.

### v1.10.1
- **Fixed: a difficulty preset no longer reverts your individual settings at every launch.** A preset
  writes eight keys and used to be re-applied on every start, silently undoing an individual toggle
  you changed afterwards - the exact edit both this file and FEATURES.md recommend. It is now written
  once per profile change. New `AppliedProfile` bookkeeping key records which preset was written;
  clear it to re-apply.
- **Fixed: the well-rested bonus explains itself when it cannot read your stats.** Six failure paths
  that previously returned in silence now each log one warning naming the lookup that failed, so
  "the bonus does nothing" is diagnosable from the log instead of a play session. Being below the
  threshold stays silent - that is the normal answer.
- **Changed: the `ActiveProfile` config text now matches the code.** It omitted two of the eight keys
  a preset writes, mis-described Immersive, and claimed Hardcore/Legacy leave "all bonuses off" when
  per-skill multipliers survive every preset. The valid-name list is now generated from the presets.
- **Docs:** removed a documented config key that does not exist (`Blade_Multiplier`) and a synergy
  example using two non-existent skills, corrected `ClubFighting`'s spelling, listed all thirteen
  features rather than nine, regenerated the configuration sample from the real config keys, and
  updated the game-version references to EA 0.67h.

### v1.10.0
- **Area familiarity floor:** new `AreaFamiliarityMinBonus` sets the bonus a location starts at before you have worked it at all. Default 0 reproduces the previous behaviour exactly; the ramp to `AreaFamiliarityMaxBonus` is unchanged, and Min is clamped to Max so it can never run backwards.
- **Effective-settings log:** new `LogEffectiveSettings` (default off) writes one line per skill after load showing the XP multiplier that actually resolved and where it came from, plus staleness state and decay rate, and two header lines covering every global toggle.
- **Difficulty profiles now set the whole stack.** Presets previously moved only ExpMultiplier and Staleness, silently leaving every feature added after v1.7 at whatever it already was. Each preset now writes all six feature toggles explicitly, including the ones it turns off. Adds an **Immersive** preset: 1x XP with staleness, no raw multiplier, and only the diegetic bonuses (familiarity, morning, daily first use, well-rested).
- **Composed-multiplier cap:** new `MaxComposedMultiplier` (default 0 = uncapped) clamps the combined total after every bonus has stacked. `LogMultiplierCapHits` logs when it bites.
- **Daily first-use bonus:** new `DailyFirstUseBonusEnabled` / `DailyFirstUseMultiplier` (default off, 1.5x) multiply each skill's first XP gain of the in-game day.
- **Well-rested bonus:** new `WellRestedBonusEnabled` / `WellRestedMultiplier` / `WellRestedThreshold` (default off, 1.25x above 60%) apply while Energy, Satiation and Hydration are all at or above the threshold percentage of their maximum.
- **Level-scaling curve:** new `LevelScalingCurve` (Linear default, EaseIn, EaseOut) reshapes the level-scaling ramp. EaseIn rewards pushing a skill toward its ceiling; EaseOut front-loads help onto low-level skills.
- Every new feature defaults to off or to a no-op value, so an existing config behaves exactly as it did on 1.9.7 until you change something.

### v1.9.7
- Removed the temporary `[MorningBonus][DIAG]` diagnostic logging added in v1.9.6 — the capture window closed without a reproduction against this mod's code path. No behavior change.
- Silent-catch reflection breadcrumbs in `MorningBonusPatch` and `AreaFamiliarityPatch` upgraded from `LogDebug` (invisible by default) to `LogWarning`, so a future field-rename that breaks bonus/familiarity tracking now shows up in `LogOutput.log`.

### v1.9.6
- Added temporary diagnostic logging (`LogInfo`) in `MorningBonusPatch` to investigate a player-reported vanilla bug (EA 0.66a+) where a large stat penalty — e.g. the -150 Stealth hit from an extinguished campfire — is allegedly misapplied as a positive XP gain. Logs the raw before/after/delta on any skill-stat change of magnitude ≥50, and separately logs if a bonus is then compounded on top of it. Informational only — no behavior change. Will be removed/demoted once the report is resolved.

### v1.9.5
- **Fixed:** Morning Bonus window was computed as "hours since day start" instead of the actual in-game clock hour, omitting `DaySettings.DayStartingHour`. `MorningStartHour`/`MorningEndHour` are now true clock hours (0 = midnight, 12 = noon) — the default 5–9 window now fires at the advertised early-morning hours instead of 09:00–13:00.

### v1.9.3–v1.9.4
- Version bumps alongside framework/in-house mod releases; no behavior change.

### v1.9.2
- Internal refactor: `AreaFamiliarityPatch`, `GameLoadPatch`, and `MorningBonusPatch` migrated their bespoke reflection helpers to the shared `CSFFModFramework.Api.Reflect`/`StatAccess`/`CardUtil` utilities, removing ~320 lines of duplicated per-mod reflection scaffolding. No behavior change.
- Version bump for release alongside framework 2.9.1 and all in-house mods

### v1.9.1
- Version bump for release alongside framework 2.0.8 and all in-house mods

### v1.9.0
- **Level Scaling** — Optional XP bonus that scales linearly from 0% at skill level 0 to `LevelScalingMaxBonus` (default +50%) at max level, compensating for the steep XP-per-level cost at higher levels
  - `LevelScalingEnabled` (default false), `LevelScalingMaxBonus` (default 0.50 = +50% at max level, range 0–3.0)
  - Stacks multiplicatively with global, per-skill, morning, area familiarity, and synergy bonuses

### v1.8.0
- **Skill Synergies** — Combo XP bonus for chaining related skills; +10% per action up to +50%, 5-min timeout. Off by default (`EnableSkillSynergies`). Debug logging via `SkillSynergiesDebugLog`.
- **Difficulty Profiles** — `ActiveProfile` config key applies a named preset (VanillaPlus, Casual, Hardcore, Grinder, Balanced, Legacy) with one change.
- **Per-Skill Staleness** — `<Skill>_UseStaleness` toggle and `<Skill>_StalenessMultiplier` (0.1–5.0) for fine-grained staleness control per skill, AND-ed with the global `EnableSkillStaleness` flag.

### v1.7.6
- EA 0.65f compatibility pass; no logic changes

### v1.7.5
- Minor fixes to area familiarity TSV persistence path

### v1.7.4
- EA 0.65 compatibility pass
- Startup log normalized to single Info line per CSFF mod logging norms

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
- **Or:** set `LogEffectiveSettings = true` and reload. The log then shows, per skill, the multiplier that actually resolved and whether it came from your per-skill override or the global setting.

**Issue:** "I picked a difficulty profile but a feature I had turned on is now off"
- **Explanation:** A profile writes every toggle it covers, including the ones it turns off, so the preset you chose is the preset you get. Set an individual key *after* choosing the profile if you want to differ from it: since 1.10.1 that edit sticks, because a preset is written ONCE per profile change rather than re-asserted on every launch. (On 1.10.0 exactly it was re-applied at every start, which silently undid such edits.)

**Issue:** "I can't tell whether a bonus is applying to a skill"
- **Fix:** Set `LogSkillXpGains = true` and relaunch the game. Every skill XP gain then writes one line to LogOutput.log with the gain the game applied, the combined multiplier and the gain after it. Turn it off again afterwards: it logs on every gain.

**Issue:** "XP is going up far faster than my settings suggest"
- **Fix:** Bonuses multiply. Set `MaxComposedMultiplier` to a ceiling you are comfortable with, and `LogMultiplierCapHits = true` to see when it bites.

For more help, see **FEATURES.md** or check BepInEx LogOutput.log.

## Credits

Created by Jared (CrispyWhips)  
Framework: BepInEx, HarmonyLib
