# Skill Speed Boost — Feature Guide

Complete documentation of all features, configuration options, and systems.

**Current Features:** Staleness control, global XP multiplier, per-skill multipliers, morning bonus, area familiarity, difficulty profiles, per-skill staleness, skill synergies, level scaling, daily first-use bonus, well-rested bonus, low-condition XP penalty, composed-multiplier cap, effective-settings log.

## Core Features

### 1. Skill Staleness Control
**What:** Toggle skill novelty penalties on/off globally.

**Settings:**
- `EnableSkillStaleness` (default: `true`) — Enable/disable all staleness penalties

**How It Works:**
- When enabled, skills decay over 3 in-game hours (12 game ticks) of non-use
- The staleness penalty reduces XP gain from that skill while it's "stale"
- When disabled, `UsesNovelty` is cleared on all skill stats so staleness never accrues

---

### 2. Global XP Multiplier
**What:** Boost all skill XP gains with a global multiplier (1-10x).

**Settings:**
- `SkillExpMultiplier` (default: `1`, allowed: `1-10`) — Global XP multiplier

**How It Works:**
- Applied to all skill XP gains at runtime (no load-time cost — fires only on skill XP gains)
- Changes to this setting take effect after reloading a save or restarting the game
- Overridden by per-skill multipliers if per-skill customization is enabled

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

**How It Works:**
- Applies an extra multiplier to all skill XP gains during the configured window
- Stacks multiplicatively with global and per-skill multipliers
- Wrap-around windows supported (e.g. start=22, end=4 = late night through early morning)

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

---

### 6. Difficulty Profiles
**What:** Named presets that set multiple settings at once with a single config key.

**Settings:**
- `ActiveProfile` (default: `"None"`) - Set to a profile name to apply it: `VanillaPlus`, `Casual`, `Hardcore`, `Grinder`, `Balanced`, `Legacy`, `Immersive`

**How It Works:**
- Applying a profile sets `SkillExpMultiplier`, `EnableSkillStaleness`, and all seven feature toggles (`AreaFamiliarityEnabled`, `LevelScalingEnabled`, `MorningBonusEnabled`, `EnableSkillSynergies`, `DailyFirstUseBonusEnabled`, `WellRestedBonusEnabled`, `LowConditionPenaltyEnabled`)
- A profile writes every one of those keys, including the ones it turns OFF. Before 1.10.0 it wrote only the first two, so a preset silently inherited whatever the feature toggles already were
- Applied ONCE per profile change: at startup the mod compares `ActiveProfile` against the `AppliedProfile` bookkeeping key and only writes the preset when they differ. Changing `ActiveProfile` while the game is running applies it immediately. Clear `AppliedProfile` to force a fresh apply
- ExpMultiplier and the toggles take effect immediately; staleness changes take effect after reloading a save
- Overriding any individual setting still works - the profile is a starting point. Edit the individual setting *after* choosing the profile, not before; since 1.10.1 that edit survives subsequent launches
- A preset covers situational bonuses only. It never writes `EnablePerSkillMultipliers` or any `<Skill>_Multiplier`, so per-skill overrides survive every preset - including `Hardcore` and `Legacy`, which turn off the mod's own situational bonuses but leave a `Smithing_Multiplier = 5` exactly where you set it

| Profile | XP | Staleness | Familiarity | Level scaling | Morning | Synergies | First use | Well-rested | Low-condition penalty |
|---------|-----|-----------|-------------|---------------|---------|-----------|-----------|-------------|-----------------------|
| VanillaPlus | 2x | on | on | off | off | off | off | off | off |
| Casual | 3x | off | on | on | on | on | on | on | off |
| Hardcore | 1x | on | off | off | off | off | off | off | **on** |
| Grinder | 10x | off | on | on | on | on | on | on | off |
| Balanced | 2x | on | on | on | off | off | off | off | off |
| Legacy | 1x | on | off | off | off | off | off | off | **on** |
| Immersive | 1x | on | on | off | on | off | on | on | **on** |

---

### 7. Per-Skill Staleness
**What:** Fine-grained staleness control per skill — toggle and rate multiplier for each skill individually.

**Settings:**
- `<Skill>_UseStaleness` (default: `true`) — Enable/disable staleness for a specific skill, AND-ed with the global `EnableSkillStaleness` flag
- `<Skill>_StalenessMultiplier` (default: `1.0`, range: `0.1–5.0`) — Multiplier applied to the staleness decay rate for that skill

**How It Works:**
- A skill's staleness only activates if BOTH the global `EnableSkillStaleness` AND its own `_UseStaleness` flag are true
- Higher `_StalenessMultiplier` means faster staleness decay (the stale penalty clears sooner)

---

### 8. Skill Synergies
**What:** Combo XP bonus for chaining related skills in sequence.

**Settings:**
- `EnableSkillSynergies` (default: `false`) — Enable the synergy system
- `SkillSynergiesDebugLog` (default: `false`) — Log synergy calculations to the BepInEx log

**How It Works:**
- Chaining related skills (e.g. Herbalism → Butchering → Cooking, which all share the Survival group) gives +10% per consecutive related action
- Capped at +50% (5-action combo); resets after 5 minutes of inactivity
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
- Two header lines cover the profile, global multiplier, staleness, and the state of every feature toggle
- One line per skill follows: the XP multiplier that will actually be used, whether it came from a per-skill override or the global setting, and the staleness state with its decay rate and resulting cooldown
- This is the first thing to turn on when a setting does not appear to be taking effect: it shows what the mod resolved, rather than what the config file asked for

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

[AreaFamiliarity]
AreaFamiliarityEnabled = true
AreaFamiliarityMaxBonus = 0.30
AreaFamiliarityMinBonus = 0
AreaFamiliarityVisitsForMaxBonus = 80

[DifficultyProfiles]
ActiveProfile = None
AppliedProfile =                 ; bookkeeping; clear it to re-apply ActiveProfile

[SkillSynergies]
EnableSkillSynergies = false
SkillSynergiesDebugLog = false

[LevelScaling]
LevelScalingEnabled = false
LevelScalingMaxBonus = 0.50
LevelScalingCurve = Linear

[DailyFirstUse]
DailyFirstUseBonusEnabled = false
DailyFirstUseMultiplier = 1.5

[WellRested]
WellRestedBonusEnabled = false
WellRestedMultiplier = 1.25
WellRestedThreshold = 60

[LowCondition]
LowConditionPenaltyEnabled = false
LowConditionThreshold = 60
LowConditionMultiplier = 0.5

[Diagnostics]
LogEffectiveSettings = false
```

---

## Performance Impact

- **Staleness configuration:** applied once at load-time (`GameLoad.LoadMainGameData` postfix), zero per-frame cost
- **Per-skill multipliers, global XP multiplier, morning bonus, area familiarity:** applied at runtime via a single `ChangeStatValue` coroutine postfix. The postfix RUNS on every stat change and APPLIES only on skill XP gains; when no bonus feature is enabled it early-outs before doing any work. Note that `EnablePerSkillMultipliers` defaults to `true`, so that early-out is not reached under stock settings (negligible per-event cost either way, ~0s load impact)
- **Area familiarity persistence:** saved every 8 XP-granting actions, flushed on quit

---

## Troubleshooting

**Issue:** "SkillExpMultiplier changed but didn't apply"
- *Solution:* Reload your save or restart the game. Changes to `.cfg` files require a restart to take effect. Ensure the spelling matches exactly (e.g. `SkillExpMultiplier`, not `SkillXpMultiplier`).

**Issue:** Per-skill settings not applying
- *Solution:* Ensure `EnablePerSkillMultipliers = true`. Check config file spelling matches skill names exactly (e.g. `Smithing`, not `smithing`).

**Issue:** Area familiarity bonus not showing
- *Solution:* Ensure `AreaFamiliarityEnabled = true`. The bonus starts at 0 and grows with repeated visits — check `AreaFamiliarity.tsv` in the config folder to see accumulated counts.
