# Roadmap: Skill Speed Boost
Version at time of writing: 1.9.5
Date: 2026-07-27
Audit score: 10/10 — PASS (consolidated 2026-07-27)

## Current State

**Theme**: A pure-C# utility mod that gives players fine-grained control over skill progression in CSFF — per-skill XP multipliers, natural staleness decay, morning/area/synergy/level-scaling bonuses, and one-key difficulty presets. For players who want to tune the grind up or down without touching content.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images. Ships 9 C# source files (`Plugin`, `DifficultyProfiles`, `SkillSynergies`, `SkillConfigManager`, `GlobalUsing`, and `Patcher/{GameLoadPatch, MorningBonusPatch, AreaFamiliarityPatch, AreaFamiliarityService}`) — all 9 advertised features wired to live code.

**Stability**: 10/10 — 0 CRITICAL, 0 DESIGN GAP, 0 genuine WARNING (the lone E16 "missing SimpEn.csv" is a documented false positive for a card-less mod). Passes `/audit-mod`, `/critical-analysis` (SOLID), and `/code-quality` (10/10).

**Open work**: None. Zero open retrospectives for SSB/SkillSpeedBoost/`crispywhips.skill_speed_boost`. One non-blocking runtime-verification debt: the v1.9.5 Morning Bonus clock-hour fix builds clean and is decomp-derived but has not been exercised in a live session.

**Framework compliance**: Uses the framework's shared reflection tier (`Api.Reflect`/`Api.StatAccess`/`Api.CardUtil`, 41 references across the three patch files — the v1.9.2 migration that removed ~320 lines of bespoke reflection). `[BepInDependency(..., SoftDependency)]` present; explicit per-class `ApplyPatch(harmony)` (no `PatchAll`); `UnpatchSelf()` on destroy. The two hot-path coroutine postfixes (`ChangeStatValue`, `ActionRoutine`) are the CLAUDE.md-sanctioned single-composition/IEnumerator-wrap pattern — there is no Tier-2 `ActionRouter`/`SpawnService` equivalent for "observe every skill-XP delta," so this is correct, not a gap. No deprecated patterns (no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no `ModLoaderVerison`).

---

## Phase 0: Stabilize  *(skipped — audit score 10/10, no open retrospectives)*

Nothing to stabilize. The mod is release-ready.

---

## Phase 1: Foundation

> Table-stakes health items. Version hygiene is already clean (1.9.5 across ModInfo/Plugin.cs/README); the only outstanding foundation item is closing the last verification debt.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Runtime-verify the v1.9.5 Morning Bonus clock-hour fix in a live session | Verification | P1 | Quick |
| (Localization) — N/A: card-less mod, all player text is BepInEx config-entry descriptions, not CSV-localized; no SimpEn/SimpCn.csv needed | — | — | — |

**Morning Bonus verification steps**: enable `MorningBonusEnabled`, keep default `MorningStartHour=5`/`MorningEndHour=9`; gain skill XP once at in-game clock hour ~06:00 (bonus should apply) and once at ~11:00 (bonus should NOT apply). Confirms the `DayStartingHour`-aware `IsMorningWindow` fires only in the advertised 05:00–09:00 window. This unblocks the Night-Owl / per-skill-window / time-window-arbitration ideas that build on the same clock math.

---

## Phase 2: Core Expansion

> The highest-value near-term additions from `Documentation/Ideas/SkillSpeedBoost/IDEAS.md`. Every one composes as a single `multiplier *= …` line inside `MorningBonusPatch.ChangeStat_Post` (iterator postfixes can't compose — there is exactly one composition point). Prioritize the three that refine already-shipped features, since they cannot regress balance at their defaults.

### `AreaFamiliarityMinBonus` — starting-familiarity floor
**What**: Add a config float (0–1, default 0) so the familiarity ramp is `Min + (Max − Min)·fraction` instead of always starting at 0. One-line change in `AreaFamiliarityService.GetMultiplier`.
**Why**: A brand-new location currently grants zero bonus, so the early game feels flat. Min=0 reproduces today's behavior exactly — arithmetically incapable of regressing the default.
**Requires**: none
**Complexity**: Quick

### Effective-Settings Summary Log
**What**: On load, emit one config-gated Info line per skill from the existing `GameLoadPatch` scan showing the resolved stack (global × per-skill multiplier, staleness on/off + rate), behind `LogEffectiveSettings` (default false).
**Why**: The README's #1 troubleshooting item is "changes didn't apply" / "per-skill settings not working." This lets a player confirm their config took effect without a decompiler. Pure logging — zero gameplay change.
**Requires**: none
**Complexity**: Quick

### Extend Difficulty Profiles to the post-v1.7 knobs
**What**: Make `DifficultyProfiles.ApplyProfileSettings` write through `AreaFamiliarityEnabled` / `LevelScalingEnabled` / `MorningBonusEnabled` / `EnableSkillSynergies` (currently only `SkillExpMultiplier` + `EnableSkillStaleness`), and add an "Immersive" preset.
**Why**: "One key sets everything" is only half true today — four newer features are untouched by any profile. Profile plumbing + `SettingChanged` fire path already exist; this is added write-through lines only.
**Requires**: none
**Complexity**: Medium

### Additional near-term bonus knobs (each one `multiplier *=` line)
**What**: Well-Rested / Well-Fed XP bonus (reads player condition stat); Level-Scaling curve toggle (`LevelScalingCurve` enum Linear/EaseIn/EaseOut); `MaxComposedMultiplier` clamp + diagnostic warn (the stacked total can silently hit 30x+); Daily First-Use "warm-up" bonus.
**Why**: Low-risk depth for the tuning audience; all default-off or default-uncapped so they can't regress balance.
**Requires**: none (Well-Rested reuses the `GameManager.Instance` reflection pattern already in `IsMorningWindow`)
**Complexity**: Quick–Medium each

---

## Phase 3: Integration & Depth

> Cross-mod hooks. All soft-dependency only — SSB stays fully functional without any partner mod.

### `SkillXpModifierService` — single public extension point *(do this first; it unlocks the rest)*
**What**: Add one public registration surface — `SkillXpModifierService.Register(Func<string skillName, float multiplier> provider)` — and have `ChangeStat_Post` fold every registered provider into the composed multiplier as one more loop.
**Why**: Every cross-mod idea below (Focus Tonic, combat combo, Study Desk, weather) currently proposes its own bespoke public setter. One provider list turns them all into consumer-side changes with zero further SSB edits. Supersedes the three separate "promote `SkillSynergies` to public" open questions.
**Requires**: design decisions on provider ordering/cap and hot-path cost (both logged in IDEAS.md)
**Complexity**: Medium

### RepeatAction (RA) — compatibility check + anti-AFK guard
**What**: (1) Verify SSB's multipliers actually fire on RA-driven repetitions; (2) if so, add optional `ExcludeAutomatedFromSynergy` so AFK looping can't cheese the +50% synergy combo.
**Why**: The synergy combo is meant to reward deliberate chaining, not automation.
**Requires**: RA behavior verification
**Complexity**: Medium

### Station / location-anchored XP bonuses (ACT, WDI, CMC)
**What**: A `StudyLocationUID`→multiplier map read via `AreaFamiliarityPatch.CurrentLocationUid` (already exposed) — grants a themed bonus while working at a designated station/desk. Any mod's CT2 station UID qualifies by being named in config.
**Why**: Gives ACT/WDI/CMC placed structures a skill-leveling payoff with no code change on their side.
**Requires**: `SkillXpModifierService` (cleanest) or a standalone config map
**Complexity**: Medium

### H&F "Focus Tonic" → temporary global XP boost
**What**: A consumable that sets a timed flag SSB reads as a bonus (e.g. +25% all-skill for one in-game day).
**Why**: Natural herb-mod synergy; a concrete first consumer of `SkillXpModifierService`.
**Requires**: `SkillXpModifierService`; H&F ships the consumable
**Complexity**: Medium

---

## Phase 4: Polish

> No art or animation work applies — the mod ships zero cards. Polish here is documentation and QoL.

| Item | What | Complexity |
|------|------|------------|
| README caveat accuracy | The "changes apply after reloading a save" caveat is correct today; revisit only if the live `NoveltyCooldownDuration` re-tune investigation (Long-term) removes it | Quick |
| Config-description clarity pass | Ensure every BepInEx config-entry description matches current behavior (this is SSB's only player-facing text surface) | Quick |

---

## Long-term Vision

> Where Skill Speed Boost should be at v2.0.

SSB's natural endpoint is a **composable skill-progression platform**: a single, well-documented public modifier-provider API (`SkillXpModifierService`) that any other mod in the suite — or a community mod — can register into, so consumables, stations, combat kills, weather, and seasons all feed the one composition point without SSB ever needing to know about them. Everything the mod does stays "one `multiplier *=` line," but the set of things that can contribute a line becomes open-ended.

**Potential major additions** (not yet justified at current scope — revisit after Phase 3):
- **Persistence layer** (small state file like `AreaFamiliarity.tsv`) — several deferred ideas (Consecutive-Day Streak, Rested-XP Bank, Level Milestones) are all blocked on the mod having no save hook today; one shared persistence file justifies the whole cluster at once.
- **Area Familiarity decay** — mirror the mod's own staleness philosophy for locations (familiarity fades after N unvisited days); needs a TSV schema change, so bundle with the persistence work.
- **Live `NoveltyCooldownDuration` re-tune without save reload** — investigate why the deep-stat-graph hot-reload was unstable (Plugin.cs:206) and whether just the two novelty fields can be re-poked mid-session; would drop the "reload to apply" caveat from all staleness/profile changes.

These live in `Documentation/Ideas/SkillSpeedBoost/IDEAS.md` (full specs, four dated generation passes).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new feature phase | Run `/audit-mod SkillSpeedBoost` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After adding a runtime bonus | Run `/critical-analysis SkillSpeedBoost` (verify the single-composition-point rule held) |
| After Phase 2 complete | Run `/export-to-repo SkillSpeedBoost` and bump minor version |
| Once the Morning Bonus fix is verified in-game | Close the runtime-verification debt in `.audit/summary.md` |

---

## Skill Cheatsheet for This Mod

```
/audit-mod SkillSpeedBoost         — full health check, updates .audit/
/critical-analysis SkillSpeedBoost — adversarial review
/build-mod SkillSpeedBoost         — build Release DLL
/deploy-mods SkillSpeedBoost       — build + deploy to game
/update-mod-version SkillSpeedBoost <ver> — bump version in all 3 files
/export-to-repo SkillSpeedBoost    — push to public repo
```
