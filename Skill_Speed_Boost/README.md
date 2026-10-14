# Skill Speed Boost

**Version:** 1.11.1
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.69c)

---

## Overview

Skill Speed Boost controls how fast skills level. Staleness wears off over time for every skill, you can set an XP multiplier per skill, and optional bonuses reward where, when and how you work.

**Language:** English only (config descriptions and log lines are English).
**Requires:** BepInEx 5.4.23.4+ and CSFFModFramework (see [Installation](#installation)).

## Features

Everything except staleness decay, area familiarity and the per-skill multipliers is off until you turn it on. Each feature's setting name is in brackets; details are under [Settings in detail](#settings-in-detail).

### Core

| Feature | What it does | Default |
|---|---|---|
| Staleness decay (`EnableSkillStaleness`) | One step of staleness wears off every 3 in-game hours, for every skill. Each skill has its own switch and rate. | On |
| XP multipliers (`SkillExpMultiplier`, `<Skill>_Multiplier`) | A global multiplier from 1x to 10x. Per skill, 0 stops XP, 1 uses the global and 2 to 10 replaces it. | 1x |
| Difficulty profiles (`ActiveProfile`) | A preset (Balanced, Casual, Hardcore, Grinder, Immersive, VanillaPlus, Legacy) sets the XP multiplier, staleness and all fourteen feature switches at once. | None |

### Time and place

| Feature | What it does | Default |
|---|---|---|
| Morning study bonus (`MorningBonusEnabled`) | 1.5x from 05:00 to 09:00 on the in-game clock, for every skill or only those in `MorningBonusSkills`. | Off |
| Night-owl bonus (`NightBonusEnabled`) | 1.5x from 22:00 to 02:00, for every skill or only those in `NightBonusSkills`. | Off |
| Area familiarity (`AreaFamiliarityEnabled`) | A bonus that grows the more you work at a location, up to +30% after 80 XP-granting actions there. An optional fade lowers it for places you stop visiting. | On, fade off |
| Station bonus (`StationBonusMap`) | Extra XP at structures you name, such as your forge or study desk. | Empty (off) |
| Season bonus (`SeasonBonusMap`) | Faster or slower XP in the seasons you name, such as Herbalism in spring. | Empty (off) |
| Environment bonus (`EnvironmentBonusMap`) | Faster or slower XP in the kinds of place you name, such as Knapping in any cave. It does not grow with visits. | Empty (off) |
| Tool and action bonus (`ToolBonusMap`, `ActionBonusMap`) | Faster or slower XP with the tools and from the actions you name, such as every skill with an axe or Herbalism from grinding. | Empty (off) |

### Condition and habit

| Feature | What it does | Default |
|---|---|---|
| Well-rested bonus (`WellRestedBonusEnabled`) | 1.25x while Energy, Satiation and Hydration are all at or above 60% of their maximum. | Off |
| Low-condition penalty (`LowConditionPenaltyEnabled`) | 0.5x while Energy, Satiation or Hydration is below 60%. It is the only setting that slows XP. | Off |
| In the Flow bonus (`FocusBonusEnabled`) | 1.2x while your Focus is In the Flow, its top band. Anything that raises Focus, such as a focus tea from another mod, also speeds learning. | Off |
| Daily first use (`DailyFirstUseBonusEnabled`) | 1.5x on each skill's first XP gain of the in-game day, so a varied day pays better than one long grind. | Off |
| Day streak (`StreakBonusEnabled`) | +2% for each consecutive in-game day you gain skill XP, up to 10 days. A day off restarts it. | Off |
| Rested-XP bank (`RestedBankEnabled`) | A skill you leave alone banks 3 doubled gains for each full unused day, up to 10. Each XP gain spends one. | Off |

### Progression

| Feature | What it does | Default |
|---|---|---|
| Skill synergies (`EnableSkillSynergies`) | +10% for each consecutive related action, up to +50% after 5, reset after 5 real minutes idle. Step, cap, timeout and groups are settings. | Off |
| Level scaling (`LevelScalingEnabled`) | A bonus that grows as a skill nears its maximum, up to +50%, on a Linear, EaseIn or EaseOut curve. | Off |
| Perk bonus (`PerkBonusEnabled`) | Faster or slower XP while you hold a perk you name. It ships set up for Community Mod Chest's seven Academy graduate perks at +25% each, which do nothing without that mod. | Off |
| Milestone burst (`MilestoneBonusEnabled`) | The first time a skill reaches 25, 50, 75, 100 and 125, that gain adds one extra skill point, once per character. | Off |
| XP overflow (`XpOverflowEnabled`) | XP a maxed skill cannot take goes, at half, to its least trained related skill instead of being lost. | Off |
| Partner skills (`PartnerSkillsFollowSettings`) | The Partner companion's skills take your staleness settings and your XP multiplier. | Off |
| Per-save progression (`PerSaveProgression`) | Familiarity, its fade, the day streak and the rested bank are kept separately for each save slot. | Off |

### Safeguards and diagnostics

| Feature | What it does | Default |
|---|---|---|
| Multiplier cap (`MaxComposedMultiplier`) | A ceiling on the combined multiplier after every bonus has stacked. | 0 (uncapped) |
| Effective-settings log (`LogEffectiveSettings`) | One line per skill at load showing the settings that actually resolved. | Off |
| XP gain log (`LogSkillXpGains`) | One line per XP gain, before and after the bonuses. | Off |
| Daily XP summary (`LogDailyXpSummary`) | One line per skill per in-game day, comparing the game's XP with what this mod made of it. | Off |
| XP readout (`XpReadoutEnabled`) | A short message near the time wheel names the bonuses that applied, such as "Skill XP x1.50: morning, well rested". It changes no XP. | Off |

### Before and after

**Vanilla:**
- Craft 5 flint knives and the XP per knife drops from ~62% to ~2%.
- Stop crafting and the staleness never wears off. It stays at ~2%.
- The only workaround is to craft something different.

**With Skill Speed Boost (default):**
- Craft 5 flint knives and the XP drops from ~62% to ~2%, as before.
- Stop crafting and one step of staleness wears off every 3 in-game hours, so the XP climbs back to ~62% over the next several hours.
- Variety still pays, but grinding one thing is no longer punished forever.

**With custom settings:**
- `Smithing_Multiplier = 5` learns Smithing 5x faster.
- `Tracking_Multiplier = 0` stops Tracking XP entirely.

## Installation

### Requirements
- [BepInEx 5.4.23.4+](https://github.com/BepInEx/BepInEx/releases)
- **CSFFModFramework** (required). The staleness settings, area familiarity and the XP bonuses run on its shared code, so without it they do not work.
- Card Survival: Fantasy Forest (EA 0.69c)

### Steps
1. Install CSFFModFramework in `BepInEx/plugins/CSFF_Mod_Framework/`.
2. Download the latest release.
3. Copy the `Skill_Speed_Boost` folder into `BepInEx/plugins/`.
4. Launch the game.

Your plugins folder should look like:
```
BepInEx/
  plugins/
    Skill_Speed_Boost/
      Skill_Speed_Boost.dll
```

## Quick Start

Install it and play. The defaults need no config: 1x XP, staleness wearing off every 3 in-game hours, per-skill settings on, and area familiarity on (up to +30% XP at a location you have worked 80 times).

To change a setting, edit `BepInEx/config/crispywhips.skill_speed_boost.cfg`, then restart the game. Examples:

**No staleness (relaxed)**
```ini
EnableSkillStaleness = false
SkillExpMultiplier = 3          ; 3x XP
```

**Vanilla+ (slight boost)**
```ini
EnableSkillStaleness = true
SkillExpMultiplier = 2          ; 2x XP
```

**Fast leveling (Smithing focus)**
```ini
SkillExpMultiplier = 2          ; 2x for every skill without its own value
Smithing_Multiplier = 5         ; 5x XP for Smithing only
Tracking_Multiplier = 0         ; no Tracking XP at all
```

**Immersive (no raw multiplier)**
```ini
ActiveProfile = Immersive       ; 1x XP, staleness on, and only the
                                ; bonuses the story explains: area
                                ; familiarity, the morning window,
                                ; daily first use, the day streak,
                                ; well-rested, the Academy graduate
                                ; perks and In the Flow. It also turns
                                ; on the low-condition penalty, so a
                                ; neglected body learns slower.
```

**A favourite workbench**
```ini
LogSkillXpGains = true          ; work at the structure once, then copy
                                ; its id from the end of the [XpGain]
                                ; line: "... at structure <id>"
StationBonusMap = <id>=1.25     ; +25% at that structure; list more as
                                ; id=1.25;other_id=1.1
```

**Learning with the seasons**
```ini
SeasonBonusMap = Spring:Herbalism=1.5;Winter=1.2
      ; Herbalism +50% in spring, every skill +20% in winter. In a
      ; season with both kinds of entry, a skill's own entry wins
```

**Learning from the land**
```ini
LogSkillXpGains = true          ; each [XpGain] line ends with
                                ; "in env <id> (<asset name>)"
EnvironmentBonusMap = Env_Cave*:Knapping=1.2;Env_Grove*=1.1
      ; Knapping +20% in any cave, every skill +10% in any grove
```

**Good tools, favourite work**
```ini
LogSkillXpGains = true          ; each [XpGain] line ends with
                                ; "by action '<name>' (key <key>) with tool <id> (tags ...)"
ToolBonusMap = tag_Axe=1.1;some_axe_id:Woodworking=1.25
      ; every skill +10% with any axe, Woodworking +25% with that one axe
ActionBonusMap = Grind=1.2
      ; every skill +20% from grinding
```

**Academy graduates learn faster**
```ini
PerkBonusEnabled = true         ; the shipped PerkBonusMap covers the seven
                                ; Community Mod Chest graduate perks:
                                ; Smithing, Fishing, Herbalism or
                                ; Woodworking +25% once you graduate.
                                ; Add any perk as perk_id:Skill=1.2
```

**A focused mind learns faster**
```ini
FocusBonusEnabled = true        ; +20% skill XP while Focus is In the
FocusBonusMultiplier = 1.2      ; Flow (72 and up on the current game)
```

**Coming back to a skill after a break**
```ini
RestedBankEnabled = true        ; every full day a skill goes unused
RestedBankGainsPerDay = 3       ; banks 3 of its gains, up to 10,
RestedBankMaxGains = 10         ; and each gain spends one at 2x
RestedBankMultiplier = 2
```

**A reward at each milestone**
```ini
MilestoneBonusEnabled = true    ; the first time a skill reaches 25, 50,
MilestoneEvery = 25             ; 75, 100 or 125, that gain adds one
MilestoneBonusXp = 1            ; skill point, once per character; no
                                ; multiplier scales it and
                                ; MaxComposedMultiplier does not cap it
```

**Nothing wasted on a mastered skill**
```ini
XpOverflowEnabled = true        ; XP a maxed skill cannot take goes, at
XpOverflowFraction = 0.5        ; half, to its least trained related
                                ; skill (same synergy group), never
                                ; past that skill's maximum
```

**See which bonuses are working**
```ini
XpReadoutEnabled = true         ; a short message near the time wheel,
XpReadoutMinSeconds = 60        ; e.g. "Skill XP x1.50: morning", when
                                ; the bonuses change; changes no XP
```

**A Partner who learns at your pace**
```ini
PartnerSkillsFollowSettings = true  ; the Partner's skills take your
                                    ; staleness and your XP multiplier;
                                    ; situational bonuses never apply
```

**One history per character**
```ini
PerSaveProgression = true       ; familiarity, fade days, the day streak
                                ; and the rested bank kept per save slot, in
                                ; BepInEx/config/SkillSpeedBoost/Slot<N>/
PerSaveResetOnNewGame = true    ; a new game in a slot clears what an
                                ; earlier (deleted) game left there
```

**Synergies that reward switching skills**
```ini
EnableSkillSynergies = true
SynergyRequireDifferentSkill = true  ; repeating one skill holds the combo,
                                     ; switching to a related skill grows it
SynergyTimeoutUnit = GameMinutes     ; count the timeout on the in-game clock
SynergyComboTimeoutMinutes = 60      ; an in-game hour between related actions
```

**Your own synergy groups**
```ini
EnableSkillSynergies = true
SynergyGroupOverrides = Melee Fighting=Combat;Perception=Hunting;Swimming=
      ; Melee Fighting (Community Mod Chest) joins Combat, Perception
      ; joins Hunting, and Swimming leaves every group
```

**A night owl who only cooks late**
```ini
NightBonusEnabled = true
NightBonusSkills = Cooking      ; the night bonus for Cooking only;
                                ; empty = every skill
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

**Checking your balance over a few days**
```ini
LogDailyXpSummary = true        ; one line per skill per in-game day:
                                ; its gains, the game's total, the
                                ; total after this mod, and the
                                ; average multiplier
```

For every setting, see **FEATURES.md**.

## Core Mechanics

### Staleness decay
Repeating an action makes it stale, and each repeat earns less XP. In vanilla, half of the 32 skills never shed that staleness on their own. Twelve skills, Fishing among them, lose one step every 3 in-game hours. Pottery, Tailoring and Weaving take 7 days a step, and Socials 30 days.

This mod gives every skill the same rate:
- One step wears off every **3 in-game hours** (12 ticks) by default, including for the skills vanilla decays more slowly.
- `<Skill>_StalenessMultiplier` (0.1 to 5) changes one skill's rate: 2.0 halves the wait, 0.5 doubles it.
- `<Skill>_UseStaleness = false` turns staleness off for one skill. `EnableSkillStaleness = false` turns it off for all.
- A change made while the game is running applies to every skill at once, and the settings are applied again at the start of every run. Turning staleness back on restores each skill's own values, which the mod records before it first changes them.

### XP multipliers
- **Global:** `SkillExpMultiplier` applies to every skill (1x to 10x).
- **Per skill:** `<Skill>_Multiplier` is 0 to 10. `0` stops XP for that skill, `1` (the default) uses the global multiplier, and 2 to 10 replaces it. Because 1 means "use the global", a skill cannot be held at exactly 1x while the global is higher.
- A change made in-game through a configuration manager plugin applies from the next XP gain. An edit to the `.cfg` file needs a full restart: BepInEx reads it once at startup, and loading a save does not re-read it.

### How bonuses stack
Every multiplier and bonus multiplies the others, so 2x global with a 1.5x morning and +30% familiarity gives 3.9x. `MaxComposedMultiplier` caps the total. The milestone burst and XP overflow are added after the multipliers, so nothing scales them and the cap does not limit them.

### Per-save progression
Area familiarity, its fade days, the day streak and the rested-XP bank live in files under `BepInEx/config/SkillSpeedBoost/`. By default every save and character shares them. Two settings in the `[PerSave]` section change that:
- **`PerSaveProgression`** (default `false`) keeps those files separately for each save slot, in `BepInEx/config/SkillSpeedBoost/Slot<N>/`, so a second character starts with none of the first one's history. Off, the mod uses the shared files exactly as before.
- The first save you **load** with the option on gets a copy of the shared files, and `PerSaveMigrated.txt` stops any other save getting one. Other saves and new games start empty. Delete `PerSaveMigrated.txt` to give the next save you load that has no slot folder yet a copy.
- The option never changes or deletes the shared files. Turning it off goes back to them as they were, without anything gained per save in the meantime.
- **`PerSaveResetOnNewGame`** (default `true`, used only while `PerSaveProgression` is on) clears what an earlier game left in a slot's folder when a new game starts there. A new game only starts in an empty slot, so those files belong to a deleted save. Loading clears nothing.
- Switching the option while playing moves to the right folder at once and counts as loading the save you are in. At the main menu the change waits for the next run. No preset changes either setting.
- If the save slot cannot be read (a game update renamed what the mod reads), LogOutput.log says so once and that run uses the shared files.
- The milestone record is not one of these files. It is always kept per save slot (in `Milestones/Slot<N>/`), is never copied, and is cleared by every new game in its slot.

## Settings in Detail

Map settings take entries separated by `;`. An entry the mod cannot read is skipped with a warning in LogOutput.log, and the rest still apply. A skill name the game has not loaded is reported once. No preset ever changes a map.

### Morning and night
- `MorningBonusMultiplier` (1.5, range 1 to 5) applies from `MorningStartHour` (5) to `MorningEndHour` (9). These are in-game clock hours: 0 is midnight, 12 is noon.
- `NightBonusMultiplier` (1.5) applies from `NightStartHour` (22) to `NightEndHour` (2), wrapping past midnight. If the two windows overlap, an hour inside both gets both.
- `MorningBonusSkills` and `NightBonusSkills` list the skills a bonus applies to. Empty means every skill.

### Area familiarity
- `AreaFamiliarityMaxBonus` (0.30, so +30%, up to 2.0) is reached after `AreaFamiliarityVisitsForMaxBonus` (80) XP-granting actions at one location. Every location of one kind shares a counter, so all Ponds count together.
- `AreaFamiliarityMinBonus` (0) is the bonus a location starts at before you have worked it. It cannot exceed the maximum.
- `AreaFamiliarityDecayEnabled` (off) fades a location's bonus once you have stayed away `AreaFamiliarityDecayAfterDays` (14) in-game days, by `AreaFamiliarityDecayPerDay` (1) actions a day. Nothing is deleted: working there again, or turning the fade off, restores the full bonus at once.

### Station
`StationBonusMap` takes `id=multiplier` entries. A structure counts only while you work at it: drop something onto it or press one of its buttons. It works for any mod's structures, with area familiarity on or off. Copy the id from the end of a `LogSkillXpGains` line.

### Season
`SeasonBonusMap` takes `Season:Skill=multiplier` entries, such as `Winter:Woodworking=1.25;Summer=1.1`. The season is Spring, Summer, Autumn or Winter (not Fall). Leave the skill out, or write `*`, for every skill; a skill's own entry beats its season's every-skill entry. A multiplier below 1 slows the skill.

### Environment
`EnvironmentBonusMap` takes `Place:Skill=multiplier` entries, such as `Env_Cave*:Knapping=1.2;Env_Grove*=1.1`. A place is the start of an environment's asset name followed by `*`, or one environment's exact asset name or id. Where several places match, the most specific one with an entry for that skill wins.

### Tool and action
`ToolBonusMap` (such as `tag_Axe=1.1;some_axe_id:Woodworking=1.25`) and `ActionBonusMap` (such as `Grind=1.2`) work the same way.

A tool is the card a drag-and-drop action uses on the other one, written as its id or a tag name. An action is its English name or its localization key. Where several keys match, the highest applies.

### Perk
`PerkBonusEnabled` switches on `PerkBonusMap`, which takes `Perk:Skill=multiplier` entries with the perk written as its id. It counts a perk picked at character creation or granted during the run. Several held perks on one skill give the highest entry, not their product.

The shipped map gives 1.25 for each Community Mod Chest Academy graduate perk: Metallurgy and Armorer (Smithing), Fishing (Fishing), Herbalism and Medicine (Herbalism), Carpentry and Architecture (Woodworking).

### Synergies
- `SynergyBonusPerAction` (0.1) is the extra XP per action in a combo, up to `SynergyMaxCombo` (5) actions. A combo ends after `SynergyComboTimeoutMinutes` (5) without a related action.
- `SynergyTimeoutUnit` counts that timeout in `RealMinutes` (default) or `GameMinutes`. On the in-game clock a long sleep ends a combo and an open menu does not. It moves in 15-minute steps, so use 30 or more.
- `SynergyRequireDifferentSkill` (off) grows a combo only when you switch to a different skill of the group. Repeating one skill, by hand or through RepeatAction, holds the combo instead.
- `SynergyGroupOverrides` (`Skill Name=GroupA,GroupB`) adds a skill to groups, moves it, or removes it with nothing after the `=`. The default, `Melee Fighting=Combat`, adds Community Mod Chest's Melee Fighting and does nothing without that mod.
- `SkillSynergiesDebugLog` logs each combo.

### Level scaling
`LevelScalingMaxBonus` (0.50, so +50% at a skill's maximum, range 0 to 3) is the top of the ramp. `LevelScalingCurve` shapes it: Linear (default), EaseIn, which rewards pushing a skill toward its ceiling, or EaseOut, which puts the help onto low skills.

### Condition
- `WellRestedMultiplier` (1.25) applies while Energy, Satiation and Hydration are all at or above `WellRestedThreshold` (60) percent of their maximum.
- `LowConditionMultiplier` (0.5, range 0 to 1) applies while any one is below `LowConditionThreshold` (60) percent. Both numbers are starting points; tune them to taste.
- At 0 a low stat stops skill XP entirely, and a gain it stops spends no daily first use, streak day or rested-bank gain.
- `FocusBonusMultiplier` (1.2) applies while Focus is In the Flow (72 and up on the current game). It uses the game's own Focus bands, so it matches what the game shows, and it stacks with the game's own In the Flow effects.
- Anything that raises Focus helps, such as Advanced Copper Tools' Focus Tea, a Herbs & Fungi Lion's Mane Broth or Community Mod Chest's inn cat. No other mod needs to change.
- `DailyFirstUseMultiplier` (1.5) applies to each skill's first XP gain of the in-game day.

### Day streak and rested bank
- `StreakBonusPerDay` (0.02, so +2%) adds up for each consecutive in-game day with skill XP, to `StreakBonusMaxDays` (10): +20% at the defaults. A day with no skill XP restarts it, and so does loading an older save or starting a new character.
- `RestedBankGainsPerDay` (3) gains are banked for each full in-game day a skill goes unused, up to `RestedBankMaxGains` (10). Each gain for that skill spends one and is multiplied by `RestedBankMultiplier` (2.0). A skill's first gain ever banks nothing, and neither does loading an earlier save.

### Milestone burst
- The first time a skill reaches each breakpoint, every `MilestoneEvery` (25) points, that gain also adds `MilestoneBonusXp` (1.0) skill points. A gain that passes two breakpoints pays one burst.
- The burst never takes a skill past its maximum, and a skill set to 0x never reaches one.
- The milestones paid are always kept per save slot, so reloading a save never pays one twice. A new game in a slot clears that slot's record.

### XP overflow
- XP a skill cannot take because it is at its maximum goes, at `XpOverflowFraction` (0.5), to the least trained skill sharing a synergy group with it that is still below its maximum. This works whether or not synergies are on. Ties go by name.
- A gain that crosses the maximum part way sends on only the part that was cut off.
- Only the receiving skill's own XP multiplier applies. No situational bonus, no cap, and no first use, streak, rested bank, milestone or synergy is claimed.
- It goes one step only and never past the receiver's maximum. A skill set to 0x neither sends nor receives any.

### XP readout
The message appears when the set of bonuses changes, and again after `XpReadoutMinSeconds` (60) real seconds while it stays the same. It shows nothing while every XP setting is off, and no preset changes it.

### Partner skills
With `PartnerSkillsFollowSettings` on, the Partner companion's skills (from a `Partner:` perk) follow your global staleness switch and the matching skill's toggle and rate. Each skill point the Partner gains by doing something gets your `SkillExpMultiplier` or that skill's multiplier. 0x stops it learning the skill.

Situational bonuses never apply to the Partner: time of day, places, tools, perks, synergy and streaks describe your own condition. Combat Experience is not a skill this mod tracks, so it keeps the game's rules. Off, the Partner's skills are exactly as the game makes them. This has been tested in game.

### Diagnostics
- `LogEffectiveSettings` writes one line per skill after load: the XP multiplier that resolved and where it came from, plus staleness state and rate. Three header lines cover the profile and every global switch.
- `LogSkillXpGains` writes one line per XP gain: the gain the game applied, the combined multiplier, the gain after it, and where it happened.
- `LogDailyXpSummary` writes one line per skill per in-game day, such as `[XpDigest] day 3: Herbalism: 2 gains, game +2.00, final +4.00 (avg x2.00)`. A day's lines appear when the first XP gain of a new day arrives, and the last day's when you quit.
- `LogMultiplierCapHits` logs each time `MaxComposedMultiplier` lowers a total.

## Affected Skills

The mod has no fixed list. At load it treats every stat that uses the same staleness flag as Fishing as a skill, and creates its per-skill settings under its in-game name.

Ten stats carry that flag without being skills and are left alone: Stress, Morale, Profile, Altered Mindstate, Mental Structure, Focus, Gratification, Loneliness, Thought Depth and Arousal. No XP or staleness setting touches your mental stats or your arousal.

That leaves these 32 vanilla skills:

**Crafting:** Smithing, Tailoring, Weaving, Cooking, Woodworking, Leatherworking, Pottery, Metalworking, Knapping, Crafting  
**Gathering:** Herbalism, Fishing, Spear Fishing  
**Combat:** Archery, Axe Fighting, Knife Fighting, ClubFighting, Spear Fighting, Rock Throwing, Sling, Handguns  
**Hunting:** Tracking, Trapping, Butchering  
**Other:** Stealth, Climbing, Swimming, Socials, Insight, Percussion, Wind Instruments, Perception

Config keys use these names exactly as spelled above, spaces included: `Knife Fighting_Multiplier` has a space, `ClubFighting_Multiplier` does not. A key that does not match a real skill name is never read. Set `LogEffectiveSettings = true` to see what resolved.

A skill added by another mod is picked up the same way, with no update to this mod. See **FEATURES.md** for per-skill details.

## Technical Details

This section is for modders.

- **Load and run start:** a `GameLoad.LoadMainGameData` postfix scans every `GameStat` (vanilla or modded) with `UsesNovelty` set, records each skill's own staleness values and applies the settings. A `GameManager.InitializeStatsAndActions` postfix applies them again at every run start.
- **Staleness:** sets `NoveltyCooldownDuration = 12` (12 ticks x 15 min = 3 hours), divided by the skill's staleness rate.
- **XP:** a `GameManager.ChangeStatValue` coroutine postfix is the single composition point. It multiplies every skill XP gain by the global or per-skill multiplier and every enabled bonus, and writes the total in whichever direction it points, so a penalty reduces the gain.
- Gains are measured on the skill's trained (base) value, so an effect that temporarily lowers a skill does not hide XP earned while it lasts.
- The milestone burst is written after the composed total, so nothing scales it and `MaxComposedMultiplier` does not cap it.
- XP overflow then writes the part of a gain the game's maximum cut off straight to a related skill's trained value, so no bonus scales it and nothing chains from it.
- With the XP readout on, the terms that moved the multiplier are collected and shown through the game's `GraphicsManager.PlayMessage`, anchored on the time wheel. With it off, nothing is collected.
- **Perk grants:** a perk's starting skill bonus is never scaled as XP. A perk granted part-way through a run is recognised by a prefix and finalizer on `GameManager.ApplyPerk`, the step every perk grant goes through.
- **Context:** a `GameManager.ActionRoutine` coroutine postfix records the placed structure, action and tool of the running action, for familiarity, the station, tool and action bonuses, and the `LogSkillXpGains` line.
- **Daily XP summary:** the totals are kept in memory only and written to LogOutput.log when a gain arrives on a different day, and the last day's when the game quits.
- **Persistence:** `BepInEx/config/SkillSpeedBoost/` holds `AreaFamiliarity.tsv` (visits per location), `AreaFamiliarityDays.tsv` (each location's last visit day, 1.11.0+), `SkillStreak.tsv` and `SkillRested.tsv`. The last two are written only once their feature is turned on.
- With `PerSaveProgression` on, each save slot keeps its own four files in a `Slot<N>` folder, chosen at every run start. The milestone record is always per slot, in `Milestones/Slot<N>/SkillMilestones.tsv`, written once a milestone is paid.
- **Saves:** the mod writes nothing into save files and can be removed at any time.

## Compatibility

- **Existing saves:** safe to add or remove at any time. No save file changes.
- **Other mods:** compatible with all CSFF mods. Nothing depends on Skill Speed Boost, and it needs only CSFFModFramework.
- **Performance:** a load-time scan of the ~32 skill stats, plus one hook that does its work only on skill XP gains. It stops at once when no XP feature and no XP log is enabled.

## Version History

Full notes are in **CHANGELOG.md**.

- **1.11.1:** fixes for skill gates on EA 0.69, the low-condition penalty at 0 and a structure bonus carrying into a new run, plus the 1.11.0 notes below.
- **1.11.0:** a dozen optional bonuses (night, station, season, environment, tool, action, perk, streak, rested bank, milestone, overflow, readout), synergy tuning, partner skills, per-save progression and a daily XP summary. 1.11.0 was never released on Nexus.
- **1.10.6:** a perk's starting skill bonus is no longer multiplied by your XP settings.

## Troubleshooting

**"Changes didn't apply after editing config"**
Quit to desktop and start the game again. BepInEx reads the `.cfg` file once, at startup, so loading a save does not pick up an edit. A change made in-game through a configuration manager plugin applies at once.

**"Per-skill settings not working"**
Check `EnablePerSkillMultipliers = true` and that the key matches the skill name exactly. A per-skill value of 1 means "use the global". Or set `LogEffectiveSettings = true` and restart: the log shows each skill's resolved multiplier and where it came from.

**"I picked a difficulty profile but a feature I had turned on is now off"**
A profile writes every switch it covers, including the ones it turns off. To differ from it, set an individual key after choosing the profile. That edit sticks, because a preset is written once per profile change.

**"I can't tell whether a bonus is applying to a skill"**
Set `LogSkillXpGains = true` and relaunch. Every skill XP gain then writes one line to LogOutput.log with the game's gain, the combined multiplier and the gain after it. Turn it off afterwards, since it logs on every gain.

Or set `XpReadoutEnabled = true` for an on-screen message naming the bonuses that applied. It shows nothing while every XP setting is off, and LogOutput.log warns once if the game's message display cannot be reached.

**"XP is going up far faster than my settings suggest"**
Bonuses multiply. Set `MaxComposedMultiplier` to a ceiling you are comfortable with, and `LogMultiplierCapHits = true` to see when it bites.

**"My station bonus does nothing"**
The id must be a placed structure's id, and it only counts while you work at that structure. Turn on `LogSkillXpGains`, earn XP there, and copy the id from the end of the `[XpGain]` line. LogOutput.log warns about any `StationBonusMap` entry it could not read.

**"My season bonus does nothing"**
Write the season as Spring, Summer, Autumn or Winter (not Fall) and the skill as the game shows it. LogOutput.log warns about every entry it could not read. With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line shows `SeasonBonus=on (N entries)`, counting only readable entries.

**"My environment bonus does nothing"**
Turn on `LogSkillXpGains`, earn XP there, and read the end of the `[XpGain]` line: `in env <id> (<asset name>)`. Use that id, that asset name, or the start of the name followed by `*`. A `*` anywhere else is skipped with a warning.

**"My tool bonus does nothing"**
Turn on `LogSkillXpGains`, do the action, and read the ` with tool <id> (tags ...)` part of the `[XpGain]` line. That card is the tool, and not always the one you dragged: grinding Acorns counts the mortar. Use its id or one of its tags.

No ` with tool` part means the action used no tool, such as a card's own button, or used it up before the XP landed. `ActionBonusMap`, keyed on the ` by action '<name>'` part, covers those. A `*` or space in a tool is skipped.

**"My perk bonus does nothing"**
`PerkBonusEnabled` must be true: the map ships filled in, so the switch turns it on. Write each perk as its id exactly as the perk's file spells it (letter case matters). With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line shows `PerkBonus=on (N perks mapped)`.

**"My day streak restarted"**
A day with no skill XP restarts it. So does loading an older save or starting a new character: by default every save shares the streak, and those move the in-game day backwards. `PerSaveProgression` gives each save slot its own.

**"My new character already has the old one's familiarity and streak"**
That is the default: one history shared by every save. Set `PerSaveProgression = true` and restart. From the next run each save slot keeps its own; the first save you load gets a copy, and new games start empty.

For more help, see **FEATURES.md** or check BepInEx LogOutput.log.

## Credits

Created by Jared (CrispyWhips)  
Framework: BepInEx, HarmonyLib
