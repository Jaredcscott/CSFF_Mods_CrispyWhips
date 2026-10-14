# Skill Speed Boost — Feature Guide

Complete documentation of all features, configuration options, and systems.

**Current Features:** Staleness control, global XP multiplier, per-skill multipliers, morning bonus, area familiarity (with an optional fade), difficulty profiles, per-skill staleness, skill synergies, level scaling, daily first-use bonus, well-rested bonus, low-condition XP penalty, composed-multiplier cap, effective-settings log, XP gain log, night-owl bonus, station bonus, day streak, daily XP summary, season bonus, environment bonus, tool and action bonus, perk bonus, per-save progression, In the Flow bonus, morning and night skill lists.

## Core Features

### 1. Skill Staleness Control
**What:** Toggle skill novelty penalties on/off globally.

**Settings:**
- `EnableSkillStaleness` (default: `true`) — Enable/disable all staleness penalties

**How It Works:**
- When enabled, skills decay over 3 in-game hours (12 game ticks) of non-use
- The staleness penalty reduces XP gain from that skill while it's "stale"
- When disabled, `UsesNovelty` is cleared on all skill stats so staleness never accrues
- A change made while the game is running (through a configuration manager plugin, or a preset) applies to every skill at once, and the current setting is applied again at the start of every run (a load or a new game)
- Turning it back on restores each skill's own staleness values (its multiplier, stack limit and on/off flag), which the mod records before it first changes them. If another mod changes those values after this one has (WikiMod's skill staleness setting does, at startup), its value is the one restored from then on
- An edit to the `.cfg` file on disk is read when the game starts, so it needs a full restart: loading a save does not re-read the file

---

### 2. Global XP Multiplier
**What:** Boost all skill XP gains with a global multiplier (1-10x).

**Settings:**
- `SkillExpMultiplier` (default: `1`, allowed: `1-10`) — Global XP multiplier

**How It Works:**
- Applied to all skill XP gains at runtime (no load-time cost — fires only on skill XP gains)
- A change made while the game is running applies from the next XP gain. An edit to the `.cfg` file needs a full restart (BepInEx reads the file once, at startup)
- Overridden by per-skill multipliers if per-skill customization is enabled
- Applies only to XP earned by playing, and so does every bonus below. A perk's starting skill bonus is never multiplied, whether your character starts with the perk or it is granted part-way through a run (a mod's reward perk, or an action or event that adds one): it lands at its printed value

**Example:** Double all XP gains:
```
Set SkillExpMultiplier = 2
```

---

### 3. Per-Skill XP Multipliers
**What:** Set individual XP multipliers for each skill — grind fast or slow by choice.

**Settings:**
- `EnablePerSkillMultipliers` (default: `true`) — Enable/disable per-skill customization
- Individual skill multipliers under `[Per-Skill Multipliers]` section

**How It Works:**
- Each skill can have a multiplier from 0-10 (0 = disable skill, 10 = maximum)
- Multiplier 0 disables XP gain for that skill entirely
- Per-skill multipliers override the global multiplier when enabled

**Example:** Different training speeds:
```
Set Smithing_Multiplier = 5  (fast crafting)
Set Archery_Multiplier = 2   (moderate ranged)
Set Tracking_Multiplier = 0  (disabled)
Set Knife Fighting_Multiplier = 3  (fast melee)
```

---

### 4. Morning Study Bonus
**What:** Optional XP multiplier applied during configurable morning hours.

**Settings:**
- `MorningBonusEnabled` (default: `false`) — Enable morning XP bonus
- `MorningBonusMultiplier` (default: `1.5`) — Extra multiplier during morning window
- `MorningStartHour` (default: `5`) — Start of morning window, matching the in-game clock (0 = midnight, 12 = noon; 0–23)
- `MorningEndHour` (default: `9`) — End of morning window (exclusive)
- `MorningBonusSkills` (default: empty) - The skills the bonus applies to, by the names the game shows, separated by commas (for example `Cooking, Herbalism`). Empty means every skill

**How It Works:**
- Applies an extra multiplier to skill XP gains during the configured window: every skill's, or only those `MorningBonusSkills` names. The list never widens the window, and a name no loaded skill has is reported once in LogOutput.log because it would match nothing
- Stacks multiplicatively with global and per-skill multipliers
- Wrap-around windows supported (e.g. start=22, end=4 = late night through early morning)
- Shares its clock with the night-owl bonus (section 16). If both are on and their windows overlap, an hour inside both gets both multipliers, one after the other. That is deliberate: every pair of bonuses in this mod stacks the same way

---

### 5. Area Familiarity
**What:** XP bonus that grows the more you work at a specific location.

**Settings:**
- `AreaFamiliarityEnabled` (default: `true`) — Enable area familiarity bonus
- `AreaFamiliarityMaxBonus` (default: `0.30`) — Max extra XP at full familiarity (+30%)
- `AreaFamiliarityMinBonus` (default: `0`, range: `0–2.0`) - Bonus a location starts at before you have worked it at all
- `AreaFamiliarityVisitsForMaxBonus` (default: `80`) — Actions to reach max bonus

**How It Works:**
- Each location (pond, forest clearing, etc.) tracks how many XP-granting actions you've done there
- Bonus scales linearly from `AreaFamiliarityMinBonus` to `AreaFamiliarityMaxBonus` as you accumulate visits. At the default Min of 0 that is the original 0%-to-Max ramp, unchanged
- Min is clamped to Max before the ramp is computed, so a config with Min above Max cannot make a fresh location worth more than a familiar one
- Persists across sessions in `BepInEx/config/SkillSpeedBoost/AreaFamiliarity.tsv`
- Stacks multiplicatively with global, per-skill, and morning bonuses

**Optional fade (1.11.0):**
- `AreaFamiliarityDecayEnabled` (default: `false`) - Let familiarity fade at places you stop visiting
- `AreaFamiliarityDecayAfterDays` (default: `14`, range: `1-365`) - In-game days away before the fade starts
- `AreaFamiliarityDecayPerDay` (default: `1`, range: `0.1-50`) - Actions' worth of familiarity lost per day after that

- The fade is worked out each time the bonus is read and is never written over your recorded visits, so working at the location again, or turning the fade off, restores the full bonus at once. With it off, the bonus is exactly what it was before 1.11.0
- Actions above `AreaFamiliarityVisitsForMaxBonus` are a buffer: the bonus only starts to drop once the fade takes the count below that number
- The day of each location's last visit is kept in `AreaFamiliarityDays.tsv` beside `AreaFamiliarity.tsv`. Locations recorded before 1.11.0 have no day yet and do not fade until you next work there
- By default familiarity is shared by every save (section 24 keeps it per save slot), so the fade counts from your last visit on whichever save that was. Switching between saves far apart in time can make a location read more or less neglected than it is on the save you are playing; a location whose last visit is "in the future" of the current save never fades

---

### 6. Difficulty Profiles
**What:** Named presets that set multiple settings at once with a single config key.

**Settings:**
- `ActiveProfile` (default: `"None"`) - Set to a profile name to apply it: `VanillaPlus`, `Casual`, `Hardcore`, `Grinder`, `Balanced`, `Legacy`, `Immersive`

**How It Works:**
- Applying a profile sets `SkillExpMultiplier`, `EnableSkillStaleness`, and all fourteen feature toggles (`AreaFamiliarityEnabled`, `LevelScalingEnabled`, `MorningBonusEnabled`, `NightBonusEnabled`, `EnableSkillSynergies`, `DailyFirstUseBonusEnabled`, `StreakBonusEnabled`, `WellRestedBonusEnabled`, `LowConditionPenaltyEnabled`, `PerkBonusEnabled`, `FocusBonusEnabled`, `RestedBankEnabled`, `MilestoneBonusEnabled`, `XpOverflowEnabled`), plus `SynergyRequireDifferentSkill`, which every preset sets to `false`
- A profile writes every one of those keys, including the ones it turns OFF. Before 1.10.0 it wrote only the first two, so a preset silently inherited whatever the feature toggles already were
- Applied ONCE per profile change: at startup the mod compares `ActiveProfile` against the `AppliedProfile` bookkeeping key and only writes the preset when they differ. Changing `ActiveProfile` while the game is running applies it immediately. Clear `AppliedProfile` to force a fresh apply
- Everything a preset writes takes effect as soon as it is written, staleness included: the staleness change is applied to every skill at once
- Overriding any individual setting still works - the profile is a starting point. Edit the individual setting *after* choosing the profile, not before; since 1.10.1 that edit survives subsequent launches
- A preset covers situational bonuses only. It never writes `EnablePerSkillMultipliers` or any `<Skill>_Multiplier`, so per-skill overrides survive every preset - including `Hardcore` and `Legacy`, which turn off the mod's own situational bonuses but leave a `Smithing_Multiplier = 5` exactly where you set it. `StationBonusMap`, `SeasonBonusMap`, `EnvironmentBonusMap`, `ToolBonusMap`, `ActionBonusMap`, `PerkBonusMap` and the familiarity-fade settings are never written either
- The night bonus, the day streak, `SynergyRequireDifferentSkill`, `PerkBonusEnabled`, `FocusBonusEnabled`, `RestedBankEnabled`, `MilestoneBonusEnabled` and `XpOverflowEnabled` joined the presets in 1.11.0. A preset you chose before upgrading is not re-applied on its own, so choose it again (or clear `AppliedProfile`) to pick them up. The synergy tuning numbers (`SynergyBonusPerAction`, `SynergyMaxCombo`, `SynergyComboTimeoutMinutes`, `SynergyTimeoutUnit`) and `SynergyGroupOverrides` are never written by a preset

| Profile | XP | Staleness | Familiarity | Level scaling | Morning | Night | Synergies | First use | Streak | Well-rested | Low-condition penalty | Perk bonus | In the Flow | Rested bank | Milestones | XP overflow |
|---------|-----|-----------|-------------|---------------|---------|-------|-----------|-----------|--------|-------------|-----------------------|------------|-------------|-------------|------------|-------------|
| VanillaPlus | 2x | on | on | off | off | off | off | off | off | off | off | off | off | off | off | off |
| Casual | 3x | off | on | on | on | on | on | on | on | on | off | on | on | on | on | on |
| Hardcore | 1x | on | off | off | off | off | off | off | off | off | **on** | off | off | off | off | off |
| Grinder | 10x | off | on | on | on | on | on | on | on | on | off | on | on | on | on | on |
| Balanced | 2x | on | on | on | off | off | off | off | off | off | off | off | off | off | off | off |
| Legacy | 1x | on | off | off | off | off | off | off | off | off | **on** | off | off | off | off | off |
| Immersive | 1x | on | on | off | on | off | off | on | on | on | **on** | on | on | off | off | off |

---

### 7. Per-Skill Staleness
**What:** Fine-grained staleness control per skill — toggle and rate multiplier for each skill individually.

**Settings:**
- `<Skill>_UseStaleness` (default: `true`) — Enable/disable staleness for a specific skill, AND-ed with the global `EnableSkillStaleness` flag
- `<Skill>_StalenessMultiplier` (default: `1.0`, range: `0.1–5.0`) — Multiplier applied to the staleness decay rate for that skill

**How It Works:**
- A skill's staleness only activates if BOTH the global `EnableSkillStaleness` AND its own `_UseStaleness` flag are true
- Higher `_StalenessMultiplier` means faster staleness decay (the stale penalty clears sooner)
- Either setting changed while the game is running applies at once; turning a skill's staleness back on restores that skill's own staleness values

---

### 8. Skill Synergies
**What:** Combo XP bonus for chaining related skills in sequence.

**Settings:**
- `EnableSkillSynergies` (default: `false`) — Enable the synergy system
- `SkillSynergiesDebugLog` (default: `false`) — Log synergy calculations to the BepInEx log
- `SynergyRequireDifferentSkill` (default: `false`): when true, only a switch to a DIFFERENT skill of the group grows a combo; repeating one skill holds it where it is and keeps it from timing out
- `SynergyBonusPerAction` (default: `0.1`, range `0` to `0.5`): extra XP for each action in a combo
- `SynergyMaxCombo` (default: `5`, range `1` to `20`): the most actions a combo counts
- `SynergyComboTimeoutMinutes` (default: `5`, range `1` to `120`): how long a combo survives without a related action
- `SynergyTimeoutUnit` (default: `RealMinutes`): `RealMinutes` counts that timeout on your own clock, `GameMinutes` on the in-game clock
- `SynergyGroupOverrides` (default: `Melee Fighting=Combat`): `Skill Name=GroupA,GroupB`, entries separated by `;`. An entry REPLACES that skill's built-in groups: it can add a skill no group lists (another mod's skill, or Perception), move a skill, or, with nothing after the `=`, take a skill out of synergies (`Swimming=`). Skill names are the names the game shows; group names ignore letter case, and a name other than the built-in eight starts a group of its own. The default puts Community Mod Chest's Melee Fighting skill in the Combat group and does nothing without that mod

**How It Works:**
- Chaining related skills (e.g. Herbalism → Butchering → Cooking, which all share the Survival group) gives `SynergyBonusPerAction` (+10%) per consecutive related action
- Each skill belongs to built-in groups (Crafting, Tools, Survival, Gathering, Hunting, Combat, Social, Performance); Perception and any skill another mod adds belong to none until `SynergyGroupOverrides` names them. A skill in two groups counts in both, and the bigger combo applies
- A `SynergyGroupOverrides` entry that cannot be read is skipped with one warning each, and a skill name no loaded skill has is reported once (except the default's Melee Fighting, which is absent on purpose without Community Mod Chest)
- Capped at `SynergyMaxCombo` actions (+50% at the defaults); resets after `SynergyComboTimeoutMinutes` (5) minutes of inactivity
- By default the same skill again counts as a related action, so grinding one skill builds the full combo (and so do RepeatAction's repeats, which go through the game's own action path). `SynergyRequireDifferentSkill` stops that
- Under `RealMinutes` an open menu can end a combo while a long in-game sleep taken inside a few real minutes keeps it. Under `GameMinutes` the clock is the in-game one, which moves in 15-minute steps, so most actions take at least 15 in-game minutes and a timeout of 30 or more suits it; loading an earlier save restarts every combo, and switching the unit while playing restarts them too
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line holds `SynergyTuning=...`, the step, cap, timeout and whether a change of skill is required, then `SynergyOverrides=...`, each override as `Skill -> Groups` (`no group` for a removal, `(not a loaded skill)` when no loaded skill has that name)
- Stacks multiplicatively with all other bonuses

---

### 9. Level Scaling
**What:** Optional XP bonus that grows as a skill approaches its maximum level, compensating for the steep XP cost at higher levels.

**Settings:**
- `LevelScalingEnabled` (default: `false`) — Enable level scaling bonus
- `LevelScalingMaxBonus` (default: `0.50`, range: `0–3.0`) — Bonus at max skill level (+50% by default)
- `LevelScalingCurve` (default: `Linear`) - Shape of the ramp: `Linear`, `EaseIn`, `EaseOut`

**How It Works:**
- At skill level 0: 0% bonus. At max level: `LevelScalingMaxBonus` bonus. The curve decides the shape in between
- `Linear` grows evenly. `EaseIn` stays low until the skill is well along and then climbs steeply, rewarding a push toward the ceiling. `EaseOut` climbs immediately and flattens, putting most of the help on low-level skills
- An unrecognised curve name falls back to `Linear` rather than switching the feature off
- Stacks multiplicatively with global, per-skill, morning, area familiarity, and synergy bonuses

---

### 10. Daily First-Use Bonus
**What:** A one-off multiplier on each skill's first XP gain of the in-game day.

**Settings:**
- `DailyFirstUseBonusEnabled` (default: `false`) - Enable the bonus
- `DailyFirstUseMultiplier` (default: `1.5`, range: `1.0–3.0`) - Multiplier for that first gain

**How It Works:**
- The first time a given skill earns XP on a given in-game day, that one gain is multiplied. Every later gain that day is normal
- Rewards touching several skills in a day over grinding one
- The ledger lives in memory and rolls over whenever the game's day counter changes, in either direction, so loading an earlier save re-arms it
- A skill whose per-skill multiplier is `0` (levelling disabled) never spends its daily claim

---

### 11. Well-Rested Bonus
**What:** An XP multiplier that applies while your character is rested, fed and watered.

**Settings:**
- `WellRestedBonusEnabled` (default: `false`) - Enable the bonus
- `WellRestedMultiplier` (default: `1.25`, range: `1.0–3.0`) - Multiplier while the condition holds
- `WellRestedThreshold` (default: `60`, range: `0–100`) - Percent of maximum each stat must reach

**How It Works:**
- Energy, Satiation AND Hydration must each be at or above `WellRestedThreshold` percent of their current maximum
- The check reads the live maximum, so perks or effects that raise a cap are respected
- If any of the three cannot be read, the bonus does not apply (it fails closed rather than granting an unverified bonus) and the mod logs one warning per session
- The verdict is cached for one real-time second, so the check costs nothing on the XP path

---

### 12. Low-Condition XP Penalty
**What:** A sub-1.0 XP multiplier that applies while your character is exhausted, starving or parched. The inverse of the well-rested bonus, and the only setting in this mod that makes XP move slower.

**Settings:**
- `LowConditionPenaltyEnabled` (default: `false`) - Enable the penalty
- `LowConditionThreshold` (default: `60`, range: `0-100`) - Percent of maximum below which a stat counts as low
- `LowConditionMultiplier` (default: `0.5`, range: `0-1`) - Multiplier while any stat is low. `1.0` means no penalty; `0` means no XP at all while a stat is low

**How It Works:**
- ANY one of Energy, Satiation or Hydration below `LowConditionThreshold` percent of its current maximum triggers it - unlike the well-rested bonus, which needs all three at or above its threshold
- Left at the shipped defaults the two features are exact opposites with no gap between them: at or above 60% on all three earns the bonus, under 60% on any one incurs the penalty
- It composes with every other multiplier rather than overriding them, so a 3x global multiplier and a 0.5x penalty award 1.5x, not 0.5x
- The gain being reduced is the one you just earned; the penalty never takes back XP you already had
- If any of the three stats cannot be read, the penalty does NOT apply (it fails closed in the player's favour) and the mod logs one `[LowCondition]` warning per session naming the lookup that failed
- Reads the same cached, live-maximum-aware condition stats as the well-rested bonus, so enabling both costs nothing extra on the XP path
- The defaults are starting points, not a balance verdict

---

### 13. Composed-Multiplier Cap
**What:** A ceiling on the combined multiplier after every bonus has stacked.

**Settings:**
- `MaxComposedMultiplier` (default: `0` = uncapped, range: `0–50`) - Highest total multiplier to award
- `LogMultiplierCapHits` (default: `false`) - Log a line each time the cap actually clamps a gain

**How It Works:**
- Every bonus in this mod is multiplicative, so several modest settings can combine into a much larger number than any one of them suggests (10x global, 1.5x morning, 1.3x familiarity, 1.5x synergy and 1.5x level scaling is already over 65x)
- The cap is applied once, at the end, after all bonuses have composed
- With logging off, the first clamp of a session still leaves a debug breadcrumb

---

### 14. Effective-Settings Log
**What:** A one-line-per-skill dump of the settings that actually resolved, written after game data loads.

**Settings:**
- `LogEffectiveSettings` (default: `false`) - Enable the dump

**How It Works:**
- Three header lines cover the profile, global multiplier, staleness, and the state of every feature toggle (the third, added in 1.11.0, holds the night bonus, the station bonus, the day streak, the familiarity fade, the daily XP summary, the synergy tuning, the synergy group overrides, the season bonus, the environment bonus, the tool bonus, the action bonus, the perk bonus, per-save progression, the In the Flow bonus and the morning and night skill lists, in that order)
- One line per skill follows: the XP multiplier that will actually be used, whether it came from a per-skill override or the global setting, and the staleness state with its decay rate and resulting cooldown
- This is the first thing to turn on when a setting does not appear to be taking effect: it shows what the mod resolved, rather than what the config file asked for

---

### 15. XP Gain Log
**What:** One log line per skill XP gain, showing what the game awarded and what the mod made of it.

**Settings:**
- `LogSkillXpGains` (default: `false`) - Enable the per-gain line

**How It Works:**
- Each line names the skill and shows the gain the game applied, the combined XP multiplier, the gain after it, and the skill's trained (base) value before and after
- When the gain happened while you were working at a placed structure, the line then says `at structure <UniqueID>`: the id to put in `StationBonusMap` (section 17)
- The line then says ` in env <UniqueID> (<asset name>)`, the environment the gain happened in: either value can go in `EnvironmentBonusMap` (section 21)
- The line ends with ` by action '<name>' (key <key>)` and, when the action used a tool, ` with tool <UniqueID> (tags ...)`: the keys `ActionBonusMap` and `ToolBonusMap` take (section 22)
- A gain handled at exactly 1x and a gain on a skill set to `0` both still write a line, so with any XP feature enabled, a gain with no line is one the mod did not handle
- Gains are measured on the trained value, so XP earned while a status holds a skill at 0 (Stealth under "Animals noticed your Actions") shows up here and gets its bonuses
- A gain as small as +0.5 cannot be read off the skill bar, which is what this is for. It logs on every gain, so turn it off again afterwards

---

### 16. Night-Owl Bonus
**What:** An XP multiplier during a night window: the morning bonus's counterpart for players who work late.

**Settings:**
- `NightBonusEnabled` (default: `false`) - Enable the night bonus
- `NightBonusMultiplier` (default: `1.5`, range: `1-5`) - Multiplier during the night window
- `NightStartHour` (default: `22`) - Start of the window, on the in-game clock (0 = midnight; 0-23)
- `NightEndHour` (default: `2`) - End of the window (exclusive)
- `NightBonusSkills` (default: empty) - The skills the bonus applies to, by the names the game shows, separated by commas (for example `Cooking`). Empty means every skill

**How It Works:**
- With `NightBonusSkills` empty every skill gets the bonus inside the window, as before; with names in it only those skills do. A name no loaded skill has is reported once in LogOutput.log, and a change made in game applies from the next XP gain. No preset writes either skill list
- A start later than the end wraps past midnight, so the default window is 22:00 to 02:00. A start equal to the end is an empty window
- Reads the in-game clock through the same code as the morning bonus, so the two windows always agree on what hour it is
- If the morning bonus is also on and the two windows overlap, both multiply, like every other pair of bonuses in this mod

---

### 17. Station Bonus
**What:** Extra XP at structures you choose: your forge, your study desk, a favourite workbench.

**Settings:**
- `StationBonusMap` (default: empty = off) - `UniqueID=multiplier` pairs separated by semicolons, e.g. `forge_uid=1.25;desk_uid=1.1`

**How It Works:**
- Applies to any skill XP gained while you work AT the named structure: dropping something onto it, or pressing one of its own buttons. Only placed structures count, not items in your hands
- To find a structure's UniqueID, set `LogSkillXpGains = true`, earn XP there once, and copy the id from the end of the `[XpGain]` line (`at structure <id>`)
- Works with any mod's structures; nothing is hard-coded, so an id for a mod you do not have installed simply never matches
- Stacks with every other multiplier, including area familiarity at the same structure, and works with familiarity switched off
- Multipliers must be above 0 and at most 10, written with a dot (`1.25`, not `1,25`). An entry that cannot be read is skipped and named in a warning in LogOutput.log; every other entry still applies
- Ids match regardless of upper or lower case. No preset ever writes this key
- Edits to the file take effect after a full game restart

---

### 18. Day Streak
**What:** A small bonus that grows for every consecutive in-game day you gain skill XP.

**Settings:**
- `StreakBonusEnabled` (default: `false`) - Enable the streak
- `StreakBonusPerDay` (default: `0.02`, range: `0-0.5`) - Extra XP per day of the streak (+2%)
- `StreakBonusMaxDays` (default: `10`, range: `1-100`) - The most days the streak counts

**How It Works:**
- Day 1 of a streak adds `StreakBonusPerDay` once, day 2 twice, and so on up to `StreakBonusMaxDays` days (+2%, +4%, +6% ... +20% at the defaults), after which it stays at the top
- One streak covers every skill: any skill XP at all keeps the day. A skill whose multiplier is `0` does not
- A day with no skill XP restarts it at day 1
- Kept in `BepInEx/config/SkillSpeedBoost/SkillStreak.tsv`, by default shared by every save and character like area familiarity (section 24 keeps it per save slot). Loading an older save or starting a new character moves the in-game day counter backwards, which restarts the streak exactly as a skipped day would. That is expected, not a fault

---

### 19. Daily XP Summary
**What:** One log line per skill per in-game day, totalling that day's XP: a balance check between nothing and the per-gain XP log (section 15).

**Settings:**
- `LogDailyXpSummary` (default: `false`) - Enable the daily lines

**How It Works:**
- Each line reads like `[XpDigest] day 3: Herbalism: 2 gains, game +2.00, final +4.00 (avg x2.00)`: the in-game day, the skill, how many XP gains it had that day, the game's own total, the total after this mod's multipliers and bonuses, and the average multiplier. The average is the second total divided by the first, so a large gain weighs more than a small one
- It counts exactly the gains `LogSkillXpGains` reports, a skill set to `0` included (its final total stays at +0.00), so the two logs always agree about a day. The `[XpGain]` line itself is unchanged. Like that log it sees only gains the mod handles: under stock settings that is every skill XP gain, but with `EnablePerSkillMultipliers` off, the global multiplier at 1 and every bonus off the mod leaves XP alone and the summary stays empty
- A day's lines are written when the first skill XP gain of a different day arrives. After a few days with no skill XP, the last day you gained any is written at your next gain. Loading an older save moves the day backwards, which also writes the day it leaves; leaving to the main menu and loading a save on the same day number adds to that day's lines
- The last day's lines are written when you quit the game
- Kept in memory only: nothing is written to disk but the log lines. If the game's day counter cannot be read, every gain counts as day 0 and the whole session's totals are written once, at quit
- It writes at most one line per skill per day, so unlike `LogSkillXpGains` it can stay on

---

### 20. Season Bonus
**What:** Faster (or slower) skill XP in the seasons you choose, for one skill or for every skill.

**Settings:**
- `SeasonBonusMap` (default: empty = off) - `Season:Skill=multiplier` pairs separated by semicolons, e.g. `Winter:Woodworking=1.25;Summer=1.1` (Woodworking +25% in winter, every skill +10% in summer)

**How It Works:**
- The season is `Spring`, `Summer`, `Autumn` or `Winter`, the four the game itself uses (`Fall` is not one of them). The skill is its name as the game shows it, such as `Herbalism` or `Woodworking`, and another mod's skill works the same way
- Leave the skill out (`Summer=1.1`), or write `*` (`Summer:*=1.1`), for every skill. In a season with both, a skill's own entry beats the every-skill entry: with `Autumn=3;Autumn:Herbalism=1.2`, Herbalism gets x1.2 in autumn and every other skill x3
- The current season is read from the game at most once a second, so the bonus follows the season within a second of it turning. If the game cannot report a season, no season bonus applies and LogOutput.log says why, once for each cause
- Multipliers must be above 0 and at most 10, written with a dot (`1.25`, not `1,25`); a value below 1 slows that skill in that season. Stacks with every other multiplier
- An entry that cannot be read is skipped and named in a warning in LogOutput.log; every other entry still applies. A skill name no loaded skill has is reported once, after the game has loaded its skills
- Names match regardless of upper or lower case. No preset ever writes this key
- A change made while the game is running (through a configuration manager plugin) applies from the next XP gain; an edit to the file needs a full game restart
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line holds `SeasonBonus=on (N entries)` or `SeasonBonus=off`

---

### 21. Environment Bonus
**What:** Faster (or slower) skill XP in the kinds of place you choose: Knapping in any cave, every skill in any grove. Unlike area familiarity, it does not grow with visits.

**Settings:**
- `EnvironmentBonusMap` (default: empty = off) - `Place:Skill=multiplier` pairs separated by semicolons, e.g. `Env_Cave*:Knapping=1.2;Env_Grove*=1.1` (Knapping +20% in every cave, every skill +10% in every grove)

**How It Works:**
- A place is one of three things. The start of an environment's asset name followed by `*` covers a whole family: the game's own environment cards are named by kind, such as `Env_Cave...`, `Env_Grove...`, `Env_River...`, `Env_Thicket...`, `Env_Clearing...` and `Env_Mine...`. An exact asset name (`Env_CaveOldHollow`) or an environment's UniqueID covers that one place
- To find them, set `LogSkillXpGains = true` and earn XP there: each `[XpGain]` line ends with ` in env <UniqueID> (<asset name>)` (section 15). The asset name is the card's internal name, not the name shown in game (the Great Cave is `Env_CaveOldHollow`)
- Leave the skill out (`Env_Grove*=1.1`), or write `*`, for every skill. Where several places match, the most specific one with an entry for that skill wins: the exact UniqueID, then the exact asset name, then the longest prefix. A place with no entry for the skill (neither its own nor an every-skill one) is passed over: with `Env_Grove*=1.2;Env_GrovePine*:Woodworking=1.5`, Woodworking gets x1.5 in a pine grove and every other skill x1.2. Within one place, a skill's own entry beats the every-skill entry
- A `*` anywhere but at the end, or a `*` alone, is not a place: that entry is skipped with a warning in LogOutput.log. Names match regardless of upper or lower case
- Multipliers must be above 0 and at most 10, written with a dot; a value below 1 slows that skill there. Stacks with every other multiplier, including area familiarity and the station bonus in the same place
- A skill name no loaded skill has is reported once, after the game has loaded its skills. If the game cannot report where you are, no environment bonus applies and LogOutput.log says why, once for each cause. No preset ever writes this key
- The place is read from the game at most once a second. A change made while the game is running applies from the next XP gain; an edit to the file needs a full game restart
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line holds `EnvironmentBonus=on (N entries)` or `EnvironmentBonus=off`

---

### 22. Tool and Action Bonus
**What:** Faster (or slower) skill XP with the tools you choose and from the actions you choose: every skill with an axe, Woodworking with one particular axe, Herbalism from grinding. You decide what "a better tool" is; the mod invents no quality scale.

**Settings:**
- `ToolBonusMap` (default: empty = off) - `Tool:Skill=multiplier` pairs separated by semicolons, e.g. `tag_Axe=1.1;some_axe_uniqueid:Woodworking=1.25` (every skill +10% with anything tagged as an axe, Woodworking +25% with that one axe)
- `ActionBonusMap` (default: empty = off) - `Action:Skill=multiplier` pairs, e.g. `Grind=1.2;Chop Wood:Woodworking=1.5`

**How It Works:**
- The tool is the card a drag-and-drop action uses on the other one, and the game decides which, whichever card you drag. An action belongs to one of the two cards and names the other, which is the tool: grinding Acorns on a Stone Mortar and Pestle is the Acorns' own action, so the mortar is the tool even when you drag the Acorns onto it. A card's own buttons use no tool, so `ToolBonusMap` never changes them
- A tool is written as its card's UniqueID or one of its tag names (`tag_Axe`). An action is written as its name as its button shows it in English, or its localization key, which stays the same in every language and across game updates
- To find them, set `LogSkillXpGains = true` and earn XP: each `[XpGain]` line ends with ` by action '<name>' (key <key>)` and, for a drag-and-drop action, ` with tool <UniqueID> (tags ...)` (section 15)
- Leave the skill out, or write `*`, for every skill. Within one key, a skill's own entry beats the every-skill entry. Where several of a thing's keys have an entry for the skill (a tool's UniqueID and its tags, or an action's name and its key), the HIGHEST one applies: they never stack with each other. A key with no entry for the skill is passed over
- The two maps stack with each other and with every other multiplier: a tool at 2 and an action at 3 give x6
- The key is split at its last `:`, so for an action whose name holds a `:` end the key with `:*` (`Cut: Fine:*=1.2`); written without it, the part after the `:` is read as a skill and reported as not loaded
- A `*` or a space in a tool, or a `*` in an action, is skipped with a warning in LogOutput.log. Names match regardless of upper or lower case
- Multipliers must be above 0 and at most 10, written with a dot; a value below 1 slows that skill. A skill name no loaded skill has is reported once, after the game has loaded its skills. If the game cannot report the action or the tool, that map gives no bonus and LogOutput.log says why, once for each cause (only with debug logging on when a game update removed the action's parameter itself, or when an action uses its tool up before the XP lands). No preset ever writes these keys
- A change made while the game is running applies from the next action; an edit to the file needs a full game restart
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line holds `ToolBonus=on (N entries), ActionBonus=on (N entries)` (or `off` for an empty map)

---

### 23. Perk Bonus
**What:** Faster (or slower) skill XP while you hold a perk you name. It ships set up for the Academy of Community Mod Chest: a character who graduated in Metallurgy learns Smithing faster.

**Settings:**
- `PerkBonusEnabled` (default: `false`) - the switch. The map below ships filled in, so this is what turns the bonus on; nothing changes for anyone until it is true
- `PerkBonusMap` (default: the seven Community Mod Chest graduate perks, each 1.25) - `Perk:Skill=multiplier` pairs separated by semicolons. The default maps Metallurgy and Armorer to Smithing, Fishing to Fishing, Herbalism and Medicine to Herbalism, and Carpentry and Architecture to Woodworking

**How It Works:**
- A perk counts whether you picked it at character creation or were granted it during the run. The graduate perks are granted during the run (by the Academy's courses, or all at once by the Graduate trait), which is why the mod reads both the character's perks and the run's granted perks
- A perk is written as its UniqueID exactly as the perk's own file spells it (`cmcperkgradmetallurgy`); letter case matters, and a `*` or a space is skipped with a warning in LogOutput.log. A perk no installed mod defines simply never matches, which is why the default does nothing without Community Mod Chest. Add any perk from any mod the same way
- Leave the skill out, or write `*`, for every skill. Within one perk, a skill's own entry beats the every-skill entry. When several perks you hold name the same skill, the HIGHEST entry applies: they never stack with each other
- It stacks with every other multiplier. Multipliers must be above 0 and at most 10, written with a dot; a value below 1 slows that skill. A skill name no loaded skill has is reported once, after the game has loaded its skills
- The perks you hold are checked at most once a second, and again at once when a perk is granted, so a perk granted a moment ago speeds the very next action. If the run's granted perks cannot be read (a game update renamed them), only perks picked at character creation count, and LogOutput.log says so once
- Casual, Grinder and Immersive turn `PerkBonusEnabled` on; no preset writes `PerkBonusMap`
- A change made while the game is running applies from the next XP gain; an edit to the file needs a full game restart
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line holds `PerkBonus=on (N perks mapped)` (or `PerkBonus=off`)

---

### 24. Per-Save Progression
**What:** Area familiarity, its fade days, the day streak and the rested-XP bank (section 26) kept separately for each save slot, so a second character does not inherit the first one's.

**Settings:**
- `PerSaveProgression` (default: `false`) - Keep the progression files per save slot. Off, every save shares one set, exactly as before
- `PerSaveResetOnNewGame` (default: `true`) - Only used while `PerSaveProgression` is on: a NEW game in a slot first clears the progression files an earlier game left in that slot's folder

**How It Works:**
- The files are `AreaFamiliarity.tsv`, `AreaFamiliarityDays.tsv`, `SkillStreak.tsv` and `SkillRested.tsv`. Off, they live in `BepInEx/config/SkillSpeedBoost/`; on, each save slot uses `BepInEx/config/SkillSpeedBoost/Slot<N>/` (N is the game's save slot number)
- The slot is read at every run start, a load or a new game, from a `GameManager.InitializeStatsAndActions` postfix; the game has already chosen the slot by then. A new game is told from a load by whether the run's save data holds any cards yet, which it does not until the run's first save
- The first save you LOAD with the option on, into a slot with no folder of its own yet, receives a copy of the shared files, and `PerSaveMigrated.txt` is written beside the shared files so no other save gets one. Delete it to give the next such save a copy instead. A new game never receives a copy: a new character has no history, and copying the old one in is what the option exists to stop. Every other save starts empty
- The shared files are never changed or deleted by this option. Turning it off goes back to them as they were, without anything gained per save in the meantime
- With `PerSaveResetOnNewGame` on, a new game clears only those four files from its slot's folder, never another file and never the folder. The game starts a new game only in an empty slot, so those files belong to a save that was deleted. A load never clears anything
- Before moving to another folder, the departing folder's unsaved familiarity, streak and rested bank are written where they were loaded from
- Turning `PerSaveProgression` on or off while playing moves to the right folder at once, and turning it on counts as loading the save you are in. At the main menu the change waits for the next run start
- If the save slot or the new-game test cannot be read, LogOutput.log says so once per cause: an unread slot keeps the shared files for that run, and an unread new-game test counts as a load, which never clears anything
- No preset writes either setting: it is how the files are stored, not a bonus
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line ends with `PerSave=on (a new game clears its slot's files)`, `PerSave=on (a new game keeps its slot's files)` or `PerSave=off`

---

### 25. In the Flow Bonus
**What:** Faster skill XP while your Focus is In the Flow, the top band of the game's own Focus stat. Anything that raises Focus then also speeds learning, such as Advanced Copper Tools' Focus Tea, a Herbs & Fungi Lion's Mane Broth or petting Community Mod Chest's inn cat, with no change to those mods.

**Settings:**
- `FocusBonusEnabled` (default: `false`) - Enable the bonus
- `FocusBonusMultiplier` (default: `1.2`, range: `1.0-3.0`) - Multiplier while Focus is In the Flow

**How It Works:**
- The band is read from the Focus stat's own statuses: the one that starts highest, which is In the Flow at 72 (of 96) on the current game. A game update that moves the band moves the bonus with it
- The value is read the way the game decides which status to show (rounded, up only past .5), so the bonus applies exactly while the game shows you In the Flow: 72 applies, 71 does not
- It stacks with every other multiplier and with the game's own In the Flow effects
- If Focus or its statuses cannot be read, the bonus does not apply (it fails closed) and LogOutput.log says why, once per cause
- The verdict is cached for one real-time second, like the well-rested check
- Focus itself stays out of the skill list: its own gains are never scaled
- Casual, Grinder and Immersive turn it on
- A change made while the game is running applies from the next XP gain; an edit to the file needs a full game restart
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line ends with `FocusBonus=on (1.2x In the Flow)` (or `FocusBonus=off`)

---

### 26. Rested-XP Bank
**What:** A skill you leave alone banks doubled gains, and the next gains for it spend them. It rewards coming back to a skill after a break, the opposite of the day streak (section 18).

**Settings:**
- `RestedBankEnabled` (default: `false`) - Enable the bank
- `RestedBankGainsPerDay` (default: `3`, range: `1-50`) - Gains banked for each full in-game day a skill goes unused
- `RestedBankMaxGains` (default: `10`, range: `1-100`) - The most gains one skill can bank
- `RestedBankMultiplier` (default: `2.0`, range: `1.0-5.0`) - Multiplier for a gain that spends a banked gain

**How It Works:**
- Each skill has its own bank and remembers the in-game day of its last XP gain. A gain first adds `RestedBankGainsPerDay` for every full day since that day, up to `RestedBankMaxGains`, then, if anything is banked, spends one and is multiplied by `RestedBankMultiplier`. At the defaults, Herbalism left alone for a day doubles its next three gains, and after four days or more its next ten
- A skill's first gain ever banks nothing, so it is never doubled. Loading an earlier save moves the day backwards, which banks nothing either; the next real day counts from the day you are on
- A skill set to `0` never spends its bank. It stacks with every other multiplier, and `MaxComposedMultiplier` caps it like any other
- Kept in `BepInEx/config/SkillSpeedBoost/SkillRested.tsv` (two lines per skill: its bank and the day of its last gain), by default shared by every save and character like the day streak; `PerSaveProgression` (section 24) keeps it per save slot. It is written whenever a gain changes it and again when you quit
- Casual and Grinder turn it on. Immersive leaves it off: its day streak rewards practising every day, which the bank works against
- A change made while the game is running applies from the next XP gain; an edit to the file needs a full game restart
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line holds `RestedBank=on (2x, 3 gains banked a day up to 10)` (or `RestedBank=off`)

---

### 27. Milestone Burst
**What:** The first time a skill reaches each breakpoint (25, 50, 75, 100 and 125 at the default), that XP gain also adds a skill point, once per character. A reload never pays a milestone twice.

**Settings:**
- `MilestoneBonusEnabled` (default: `false`) - Enable the burst
- `MilestoneEvery` (default: `25`, range: `5-150`) - The spacing of the breakpoints, in skill points
- `MilestoneBonusXp` (default: `1.0`, range: `0.1-25`) - Skill points added once when a skill reaches a breakpoint

**How It Works:**
- A gain reaches a breakpoint when the skill's trained (base) value was below it and is now at or above it, after every multiplier: landing exactly on 25 reaches it, starting on 25 does not. A gain that passes two breakpoints pays one burst, for the higher one
- The burst is ADDED after the composed multiplier is written, so no multiplier scales it and `MaxComposedMultiplier` does not cap it. It never takes a skill past its maximum, and a skill set to `0` never reaches a breakpoint
- Each skill remembers the highest breakpoint it has been paid, and a breakpoint at or below that is never paid again, so loading an earlier save and crossing 25 a second time pays nothing
- That record is kept per save slot whatever `PerSaveProgression` says, in `BepInEx/config/SkillSpeedBoost/Milestones/Slot<N>/SkillMilestones.tsv` (one line per skill: its name and the breakpoint number), because a record shared by every save would stop a second character from being paid. It is not one of section 24's files: it never lives in those `Slot<N>` folders and is never copied
- A NEW game started in a slot clears that slot's record, whether `PerSaveProgression` is on or off and whatever `PerSaveResetOnNewGame` says: the game starts a new game only in an empty slot, so a record found there belongs to a deleted save's character. Loading a save never clears it. If the save slot cannot be read, the record for that run is kept in `Milestones/` itself and LogOutput.log says so once
- It is written whenever a milestone is paid and again when you quit
- With `LogSkillXpGains` on, each milestone writes a line such as `[Milestone] Herbalism: reached 25 (breakpoint 1), +1.00 XP, base 25.50 -> 26.50`, and the gain's `[XpGain]` line reports the base with the burst
- Casual and Grinder turn it on. Immersive leaves it off: a reward at a round number is not something the character's world explains
- A change made while the game is running applies from the next XP gain; an edit to the file needs a full game restart
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line ends with `Milestone=on (+1 XP at every 25 points, once per character)` (or `Milestone=off`)

---

### 28. XP Overflow
**What:** XP a skill cannot take because it is at its maximum is not lost: a share of it goes to a related skill that is still below its own maximum.

**Settings:**
- `XpOverflowEnabled` (default: `false`) - Enable the redirect
- `XpOverflowFraction` (default: `0.5`, range: `0.05-1`) - The share of the excess XP that is sent on

**How It Works:**
- The excess is what the game asked to add minus what reached the skill's trained (base) value: all of it when the skill was already at its maximum, the part cut off when a gain crosses the maximum part way
- "Related" means sharing a synergy group (the same table skill synergies use, `SynergyGroupOverrides` included), whether or not `EnableSkillSynergies` is on. Herbalism's related skills, for example, are Butchering, Climbing, Cooking, Fishing, Spear Fishing, Swimming and Trapping (the Gathering and Survival groups). A skill in no group sends nothing
- The receiving skill is the related one with the lowest trained value as a share of its maximum, ties by name; a skill at its maximum is never chosen, and when every related skill is maxed nothing happens
- The amount is excess x `XpOverflowFraction` x the receiving skill's own `SkillExpMultiplier` (or its per-skill multiplier). No situational bonus applies, `MaxComposedMultiplier` does not cap it, and it counts as no practice: no daily first use, streak day, rested-bank gain, milestone or synergy combo is claimed for it
- It is written straight to the receiving skill's trained value, never past its maximum, and goes one step only: XP redirected to a skill never overflows again
- A skill set to `0` (levelling disabled) neither sends nor receives overflow
- With `LogSkillXpGains` on, each redirect writes a line such as `[XpOverflow] Herbalism: excess +1.00 x0.5 x1 to Cooking, base 10.00 -> 10.50`
- Casual and Grinder turn it on. Immersive leaves it off: XP passing from a mastered skill to its neighbour is not something the character's world explains
- A change made while the game is running applies from the next XP gain
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line ends with `XpOverflow=on (50% of the XP a maxed skill cannot take, to its least trained related skill)` (or `XpOverflow=off`)

---

### 29. XP Readout
**What:** A short on-screen message near the time wheel names the XP bonuses that applied to a skill gain, such as `Skill XP x1.50: morning, well rested`, so you can see which ones are working without reading the log. It only reports: it changes no XP.

**Settings:**
- `XpReadoutEnabled` (default: `false`) - Show the message
- `XpReadoutMinSeconds` (default: `60`, range: `0-3600`) - Real seconds before the same set of bonuses is shown again; `0` shows it on every gain

**How It Works:**
- Each gain this mod handles is checked: a bonus is named only when it actually changed that gain's multiplier, so a map with no entry for the skill or a window that is closed is left out
- The names, in the order the bonuses apply: `xN setting` (your global or per-skill multiplier above 1), `morning`, `night`, `area familiarity xN`, `station`, `season`, `place`, `tool`, `action`, `graduate perk`, `synergy combo`, `skill level`, `first use today`, `N-day streak`, `rested bank`, `well rested`, `in the flow`, `low condition` and `capped` (when `MaxComposedMultiplier` cut the total). The number after `Skill XP x` is the combined multiplier
- A message appears when the set of named bonuses differs from the last one shown, so moving from morning into a plain afternoon shows `Skill XP x1.00: no bonus` once; the same set is shown again only after `XpReadoutMinSeconds`. A first gain with no bonus shows nothing, and an unchanged "no bonus" is never repeated
- The milestone burst and XP overflow add XP rather than multiply it, so they are not named
- It reports only gains this mod handles: while every XP setting is off, nothing is shown
- The message uses the game's own floating-message display, so the mod still ships no card, stat or text file. If that display cannot be reached (early in a run, or after a game update renamed it), LogOutput.log warns once per cause and no message is shown; XP is never affected
- With `LogSkillXpGains` on, each message shown also writes a line such as `[Readout] Herbalism: Skill XP x1.50: morning`
- No preset changes it. A change made while the game is running applies from the next XP gain
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line ends with `XpReadout=on (a toast when the bonuses change, the same ones again after 60 s)` (or `XpReadout=off`)

---

### 30. Partner Skills Follow Your Settings
**What:** The Partner companion (the one a `Partner:` perk such as Partner: The Builder brings) has 32 skills of its own, which the game raises when the Partner works a card action, such as grinding with a mortar. This option makes those skills follow your settings: your staleness rules, and your XP multiplier on each skill point the Partner gains.

**Settings:**
- `PartnerSkillsFollowSettings` (default: `false`) - Let the Partner's skills follow your settings

**How It Works:**
- Staleness: each Partner skill takes the rule your matching skill has (`EnableSkillStaleness`, and that skill's `_UseStaleness` toggle and `_StalenessMultiplier` rate), so with staleness on its staleness now wears off every 3 in-game hours at rate 1, as yours does
- XP: each skill point the Partner gains is multiplied by your `SkillExpMultiplier`, or by the matching skill's per-skill multiplier when `EnablePerSkillMultipliers` is on (0x stops the Partner learning that skill), never past the skill's maximum
- Situational bonuses never apply to the Partner: no morning or night bonus, area familiarity, station, season, place, tool, action, perk, synergy, level scaling, first use, streak, rested bank, well rested, In the Flow, low condition, `MaxComposedMultiplier`, milestone or XP overflow. They describe your own condition, not the Partner's
- The Partner's skills are matched to yours by name, which the game spells differently (its `WoodWorking` is your Woodworking, its `Spearfishing` your Spear Fishing). 31 of the 32 match; Combat Experience is not a skill this mod tracks, so the Partner's keeps the game's own rules, and LogOutput.log says so once while the option is on
- Off, the Partner's skills are exactly as the game makes them, and switching the option off gives them back their own staleness rules at once
- With `LogSkillXpGains` on, each Partner skill gain writes a line such as `[PartnerXp] Herbalism: game +1.00 x3 = +3.00, base 0.00 -> 3.00`
- No preset changes it. A change made while the game is running applies at once
- With `LogEffectiveSettings` on, the third `[EffectiveSettings]` line ends with `PartnerSkills=on (...)` (or `PartnerSkills=off`)
- Checked by an automated test-harness run (T1.316); not yet verified in a human playthrough

---

## Configuration File Example

Every key below, with its shipped default. Sections match the `.cfg` exactly.

```ini
[Staleness]
EnableSkillStaleness = true

[Experience]
SkillExpMultiplier = 1
EnablePerSkillMultipliers = true
MaxComposedMultiplier = 0        ; 0 = uncapped
LogMultiplierCapHits = false

[Per-Skill Multipliers]
Smithing_Multiplier = 5
Archery_Multiplier = 2
Knife Fighting_Multiplier = 3
Tracking_Multiplier = 0

[Per-Skill Staleness]
Smithing_UseStaleness = true

[Per-Skill Staleness Multipliers]
Smithing_StalenessMultiplier = 1.0

[MorningBonus]
MorningBonusEnabled = false
MorningBonusMultiplier = 1.5
MorningStartHour = 5
MorningEndHour = 9
MorningBonusSkills =

[NightBonus]
NightBonusEnabled = false
NightBonusMultiplier = 1.5
NightStartHour = 22
NightEndHour = 2
NightBonusSkills =

[AreaFamiliarity]
AreaFamiliarityEnabled = true
AreaFamiliarityMaxBonus = 0.30
AreaFamiliarityMinBonus = 0
AreaFamiliarityVisitsForMaxBonus = 80
AreaFamiliarityDecayEnabled = false
AreaFamiliarityDecayAfterDays = 14
AreaFamiliarityDecayPerDay = 1

[StationBonus]
StationBonusMap =                ; empty = off; e.g. forge_uid=1.25;desk_uid=1.1

[SeasonBonus]
SeasonBonusMap =                 ; empty = off; e.g. Winter:Woodworking=1.25;Summer=1.1

[EnvironmentBonus]
EnvironmentBonusMap =            ; empty = off; e.g. Env_Cave*:Knapping=1.2;Env_Grove*=1.1

[ToolBonus]
ToolBonusMap =                   ; empty = off; e.g. tag_Axe=1.1;some_axe_uniqueid:Woodworking=1.25

[ActionBonus]
ActionBonusMap =                 ; empty = off; e.g. Grind=1.2;Chop Wood:Woodworking=1.5

[PerkBonus]
PerkBonusEnabled = false
PerkBonusMap = cmcperkgradmetallurgy:Smithing=1.25;cmcperkgradarmorer:Smithing=1.25;cmcperkgradfishing:Fishing=1.25;cmcperkgradherbalism:Herbalism=1.25;cmcperkgradmedicine:Herbalism=1.25;cmcperkgradcarpentry:Woodworking=1.25;cmcperkgradarchitecture:Woodworking=1.25

[PerSave]
PerSaveProgression = false
PerSaveResetOnNewGame = true

[DifficultyProfiles]
ActiveProfile = None
AppliedProfile =                 ; bookkeeping; clear it to re-apply ActiveProfile

[SkillSynergies]
EnableSkillSynergies = false
SkillSynergiesDebugLog = false
SynergyRequireDifferentSkill = false
SynergyBonusPerAction = 0.1
SynergyMaxCombo = 5
SynergyComboTimeoutMinutes = 5
SynergyTimeoutUnit = RealMinutes  ; or GameMinutes
SynergyGroupOverrides = Melee Fighting=Combat

[LevelScaling]
LevelScalingEnabled = false
LevelScalingMaxBonus = 0.50
LevelScalingCurve = Linear

[DailyFirstUse]
DailyFirstUseBonusEnabled = false
DailyFirstUseMultiplier = 1.5

[Streak]
StreakBonusEnabled = false
StreakBonusPerDay = 0.02
StreakBonusMaxDays = 10

[RestedBank]
RestedBankEnabled = false
RestedBankGainsPerDay = 3
RestedBankMaxGains = 10
RestedBankMultiplier = 2

[Milestone]
MilestoneBonusEnabled = false
MilestoneEvery = 25
MilestoneBonusXp = 1

[XpOverflow]
XpOverflowEnabled = false
XpOverflowFraction = 0.5

[XpReadout]
XpReadoutEnabled = false
XpReadoutMinSeconds = 60

[PartnerSkills]
PartnerSkillsFollowSettings = false

[WellRested]
WellRestedBonusEnabled = false
WellRestedMultiplier = 1.25
WellRestedThreshold = 60

[FocusBonus]
FocusBonusEnabled = false
FocusBonusMultiplier = 1.2

[LowCondition]
LowConditionPenaltyEnabled = false
LowConditionThreshold = 60
LowConditionMultiplier = 0.5

[Diagnostics]
LogEffectiveSettings = false
LogSkillXpGains = false
LogDailyXpSummary = false
```

---

## Performance Impact

- **Staleness configuration:** applied at load time (`GameLoad.LoadMainGameData` postfix), again at every run start (`GameManager.InitializeStatsAndActions` postfix) and whenever a staleness setting changes in-game: at most four field writes per skill each time, zero per-frame cost
- **Per-skill multipliers, global XP multiplier, morning bonus, area familiarity:** applied at runtime via a single `ChangeStatValue` coroutine postfix. The postfix RUNS on every stat change and APPLIES only on skill XP gains; when no bonus feature is enabled it early-outs before doing any work. Note that `EnablePerSkillMultipliers` defaults to `true`, so that early-out is not reached under stock settings (negligible per-event cost either way, ~0s load impact)
- **Area familiarity persistence:** saved every 8 XP-granting actions, flushed on quit
- **Day streak persistence:** written once when the streak changes (at most once per in-game day) and flushed on quit
- **Rested-XP bank persistence:** off by default; when on, `SkillRested.tsv` is written when a gain changes a skill's bank or its last day (at most once per skill per day, plus once per banked gain spent) and flushed on quit
- **Milestone burst:** off by default; when on, one breakpoint test per skill XP gain, and `SkillMilestones.tsv` written only when a milestone is paid (at most a handful of times per skill per character) and on quit
- **XP overflow:** off by default; when on, every skill XP gain also reads the value the game asked to add, and only a gain the maximum cut off goes on to look up the related skills (one stat lookup each) and write one value; nothing is saved
- **XP readout:** off by default, when it costs one bool test per skill XP gain and allocates nothing; when on, one small list of term names per gain, and at most one on-screen message when the bonuses change (or once a minute while they stay the same); the game's display members are looked up once and nothing is saved
- **Per-save progression:** off by default, when a run start only reads the save slot and the new-game test and writes nothing; when on, a run start that changes folder (or starts a new game) checks the folder, copies or deletes at most four small files and re-reads the familiarity, streak and rested-bank files. Nothing per XP gain
- **Station bonus:** the map is parsed once at startup, not per XP gain
- **Perk grants:** a prefix and finalizer on `GameManager.ApplyPerk` raise and lower one counter each time the game applies a perk; no per-frame cost
- **Daily XP summary:** off by default; when on, one in-memory total per skill updated on each skill XP gain, and one log line per skill per in-game day; nothing written to disk but the log

---

## Troubleshooting

**Issue:** "SkillExpMultiplier changed but didn't apply"
- *Solution:* An edit to the `.cfg` file needs a full restart of the game; loading a save is not enough, because BepInEx reads the file once, at startup. A change made in-game through a configuration manager plugin applies from the next XP gain. Ensure the spelling matches exactly (e.g. `SkillExpMultiplier`, not `SkillXpMultiplier`).

**Issue:** Per-skill settings not applying
- *Solution:* Ensure `EnablePerSkillMultipliers = true`. Check config file spelling matches skill names exactly (e.g. `Smithing`, not `smithing`).

**Issue:** Area familiarity bonus not showing
- *Solution:* Ensure `AreaFamiliarityEnabled = true`. The bonus starts at 0 and grows with repeated visits — check `AreaFamiliarity.tsv` in the config folder to see accumulated counts. If `AreaFamiliarityDecayEnabled` is on, a location you have not worked at for a while reads lower until you work there again.

**Issue:** Station bonus not applying
- *Solution:* The id must be a placed structure's UniqueID and you must be working AT it. Copy the id from the end of an `[XpGain]` line with `LogSkillXpGains = true`, check LogOutput.log for a `[StationBonus]` warning about your entry, and restart the game after editing the file.

**Issue:** Day streak reset unexpectedly
- *Solution:* A day with no skill XP restarts the streak. So does loading an older save or starting a new character, because by default the streak is shared by every save and those move the in-game day counter backwards. This is expected, not a fault. `PerSaveProgression` (section 24) gives each save slot its own streak.
