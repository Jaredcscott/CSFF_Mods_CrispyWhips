# Skill Speed Boost — Feature Guide

Complete documentation of all features, configuration options, and systems.

**Current Features:** Staleness control, global XP multiplier, per-skill multipliers, morning bonus, area familiarity, difficulty profiles, per-skill staleness, skill synergies, level scaling.

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

### 6. Difficulty Profiles
**What:** Named presets that set multiple settings at once with a single config key.

**Settings:**
- `ActiveProfile` (default: `""`) — Set to a profile name to apply it: `VanillaPlus`, `Casual`, `Hardcore`, `Grinder`, `Balanced`, `Legacy`

**How It Works:**
- Applying a profile sets `SkillExpMultiplier` and `EnableSkillStaleness` to preset values
- Overriding any individual setting still works — the profile is a starting point

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
- Chaining related skills (e.g. Foraging → Herbal → Cooking) gives +10% per consecutive related action
- Capped at +50% (5-action combo); resets after 5 minutes of inactivity
- Stacks multiplicatively with all other bonuses

---

### 9. Level Scaling
**What:** Optional XP bonus that grows as a skill approaches its maximum level, compensating for the steep XP cost at higher levels.

**Settings:**
- `LevelScalingEnabled` (default: `false`) — Enable level scaling bonus
- `LevelScalingMaxBonus` (default: `0.50`, range: `0–3.0`) — Bonus at max skill level (+50% by default)

**How It Works:**
- At skill level 0: 0% bonus. At max level: `LevelScalingMaxBonus` bonus. Scales linearly between.
- Stacks multiplicatively with global, per-skill, morning, area familiarity, and synergy bonuses

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
- *Solution:* Reload your save or restart the game. Changes to `.cfg` files require a restart to take effect. Ensure the spelling matches exactly (e.g. `SkillExpMultiplier`, not `SkillXpMultiplier`).

**Issue:** Per-skill settings not applying
- *Solution:* Ensure `EnablePerSkillMultipliers = true`. Check config file spelling matches skill names exactly (e.g. `Smithing`, not `smithing`).

**Issue:** Area familiarity bonus not showing
- *Solution:* Ensure `AreaFamiliarityEnabled = true`. The bonus starts at 0 and grows with repeated visits — check `AreaFamiliarity.tsv` in the config folder to see accumulated counts.
