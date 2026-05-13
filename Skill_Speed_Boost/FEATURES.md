# Skill Speed Boost — Feature Guide

Complete documentation of all features, configuration options, and systems.

**Current Features:** Staleness control, global XP multiplier, per-skill multipliers, morning bonus, area familiarity.

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
- Applied to all skill XP gains at runtime
- Changes take effect immediately — multipliers are applied at runtime via the `ChangeStatValue` postfix
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
Set Blade_Multiplier = 3     (fast melee)
```

---

### 4. Morning Study Bonus
**What:** Optional XP multiplier applied during configurable morning hours.

**Settings:**
- `MorningBonusEnabled` (default: `false`) — Enable morning XP bonus
- `MorningBonusMultiplier` (default: `1.5`) — Extra multiplier during morning window
- `MorningStartHour` (default: `5`) — Start of morning window (game-hours, 0–23)
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
- `AreaFamiliarityVisitsForMaxBonus` (default: `80`) — Actions to reach max bonus

**How It Works:**
- Each location (pond, forest clearing, etc.) tracks how many XP-granting actions you've done there
- Bonus scales linearly from 0% to `AreaFamiliarityMaxBonus` as you accumulate visits
- Persists across sessions in `BepInEx/config/SkillSpeedBoost/AreaFamiliarity.tsv`
- Stacks multiplicatively with global, per-skill, and morning bonuses

---

## Configuration File Example

```ini
[Staleness]
EnableSkillStaleness = true

[Experience]
SkillExpMultiplier = 1
EnablePerSkillMultipliers = true

[Per-Skill Multipliers]
Smithing_Multiplier = 5
Archery_Multiplier = 2
Blade_Multiplier = 3
Tracking_Multiplier = 0

[MorningBonus]
MorningBonusEnabled = false
MorningBonusMultiplier = 1.5
MorningStartHour = 5
MorningEndHour = 9

[AreaFamiliarity]
AreaFamiliarityEnabled = true
AreaFamiliarityMaxBonus = 0.30
AreaFamiliarityVisitsForMaxBonus = 80
```

---

## Performance Impact

- **Staleness configuration:** applied once at load-time (`GameLoad.LoadMainGameData` postfix), zero per-frame cost
- **Per-skill multipliers, global XP multiplier, morning bonus, area familiarity:** applied at runtime via a single `ChangeStatValue` coroutine postfix — fires only on skill XP gains (negligible per-event cost, ~0s load impact)
- **Area familiarity persistence:** saved every 8 XP-granting actions, flushed on quit

---

## Troubleshooting

**Issue:** "SkillExpMultiplier changed but didn't apply"
- *Solution:* Config values are live-bound — changes to the `.cfg` file take effect on the next skill XP gain without restarting. Ensure you saved the config file and check the spelling matches exactly (e.g. `SkillExpMultiplier`, not `SkillXpMultiplier`).

**Issue:** Per-skill settings not applying
- *Solution:* Ensure `EnablePerSkillMultipliers = true`. Check config file spelling matches skill names exactly (e.g. `Smithing`, not `smithing`).

**Issue:** Area familiarity bonus not showing
- *Solution:* Ensure `AreaFamiliarityEnabled = true`. The bonus starts at 0 and grows with repeated visits — check `AreaFamiliarity.tsv` in the config folder to see accumulated counts.
