# Skill Speed Boost v2.0 — Feature Guide

Complete documentation of all features, configuration options, and systems.

**Current Features:** Staleness control, global XP multiplier, per-skill multipliers, difficulty profiles.

## Core Features

### 1. Skill Staleness Control
**What:** Toggle skill novelty penalties on/off and customize staleness behavior per-skill.

**Settings:**
- `EnableSkillStaleness` (default: `true`) — Enable/disable all staleness penalties
- Per-skill staleness toggles — Each skill can have staleness enabled/disabled independently
- Per-skill staleness multipliers — Customize how fast each skill's novelty decays

**How It Works:**
- When enabled, skills decay over 3 in-game hours (12 game ticks) of non-use
- The staleness penalty reduces XP gain from that skill while it's "stale"
- Use per-skill multipliers to make certain skills decay faster (e.g., combat slower, crafting faster)

**Example:** Make Smithing decay 2x faster while Archery decays 0.5x slower:
```
Set Smithing_StalenessMultiplier = 2.0
Set Archery_StalenessMultiplier = 0.5
```

---

### 2. Global XP Multiplier
**What:** Boost all skill XP gains with a global multiplier (1-10x).

**Settings:**
- `SkillExpMultiplier` (default: `1`, allowed: `1-10`) — Global XP multiplier

**How It Works:**
- Applied to all skill stat modifiers that grant XP
- Changes take effect after reloading a save or restarting the game
- Overridden by per-skill multipliers if per-skill customization is enabled

**Example:** Double all XP gains:
```
Set SkillExpMultiplier = 2
```

---

### 3. Per-Skill XP Multipliers (NEW in v2.0)
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

### 4. Difficulty Profiles (NEW in v2.0)
**What:** Pre-configured settings for different playstyles — switch with one setting.

**Available Profiles:**
- **VanillaPlus** — 2x XP, vanilla staleness (slight boost, maintain feel)
- **Casual** — 3x XP, no staleness (relaxed grinding)
- **Hardcore** — 1x XP, full staleness (vanilla difficulty)
- **Grinder** — 10x XP, no staleness (testing/sandbox)
- **Balanced** — 2x XP, manageable staleness (recommended)
- **Legacy** — 1x XP, full staleness (original vanilla behavior)

**How It Works:**
- Set `ActiveProfile` to switch all settings at once
- Profiles override individual settings on game load

**Example:** Switch to Casual mode:
```
Set ActiveProfile = Casual
```

---

## Configuration File Example

```ini
[Staleness]
EnableSkillStaleness = true

[Experience]
SkillExpMultiplier = 1
EnablePerSkillMultipliers = true

[Profiles]
ActiveProfile = Balanced

[Per-Skill Multipliers]
Smithing_Multiplier = 5
Archery_Multiplier = 2
Blade_Multiplier = 3
Tracking_Multiplier = 0

[Per-Skill Staleness]
Smithing_UseStaleness = true
Archery_UseStaleness = false
Blade_UseStaleness = true

[Per-Skill Staleness Multipliers]
Smithing_StalenessMultiplier = 2.0
Archery_StalenessMultiplier = 0.5
Blade_StalenessMultiplier = 1.0
```

---

## Advanced Usage

### Dynamic Skill Registration
Register skills not in core list:
```csharp
SkillConfigManager.RegisterDynamicSkill(config, "CustomSkill");
```

---

## Frequently Asked Questions

**Q: Will per-skill multipliers break balance?**
A: No. The game's challenge comes from learning speed and resource management. Multipliers only affect XP gain, not actual gameplay difficulty. Use what feels right for your playstyle.

**Q: How do I disable XP grinding for a specific skill?**
A: Set that skill's multiplier to `0` in the `[Per-Skill Multipliers]` section.

**Q: Do profiles reset my per-skill settings?**
A: Profiles only apply their preset multipliers on game load. Manual per-skill settings override profiles.

---

## Performance Impact

- **Per-skill multipliers:** No runtime cost (applied at load time)

All features are applied once at game-load time and incur zero per-frame overhead.

---

## Version History

### v2.0.0 (Current)
- Added per-skill XP multipliers
- Added difficulty profiles (VanillaPlus, Casual, Hardcore, Grinder, Balanced, Legacy)
- Updated to support all configuration systems

### v1.5.0
- Global XP multiplier (1-10x)
- Staleness enable/disable
- Fixed novelty cooldown to 12 ticks (3 in-game hours)

---

## Troubleshooting

**Issue:** "SkillExpMultiplier changed but didn't apply"
- *Solution:* Changes require reloading a save or restarting the game. Hot-reload is disabled for stability.

**Issue:** Per-skill settings not applying
- *Solution:* Ensure `EnablePerSkillMultipliers = true`. Check config file spelling matches `CoreSkills` names.

---

For detailed implementation info, see the code comments in:
- `SkillConfigManager.cs` — Per-skill and profile management
- `Patcher/GameLoadPatch.cs` — Runtime Harmony patch applying multipliers and staleness settings
- `Plugin.cs` — Configuration registration

## Planned Features (not yet implemented)

See [`Documentation/Ideas/SkillSpeedBoost/`](../Documentation/Ideas/SkillSpeedBoost/) at the repo root for specs on future work:
- **Skill Synergies** — combo XP bonuses for chaining related skills
