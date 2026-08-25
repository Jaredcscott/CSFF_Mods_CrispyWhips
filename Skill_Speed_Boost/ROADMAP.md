# Roadmap: Skill Speed Boost
Version at time of writing: 1.9.7
Date: 2026-08-24
Audit score: 10/10 — PASS (consolidated 2026-08-24)

## Current State

**Theme**: A pure-C# utility mod that gives players fine-grained control over skill progression in CSFF — per-skill XP multipliers (0–10x), natural staleness decay (global or per-skill), and stacking morning / area-familiarity / synergy / level-scaling bonuses plus one-key difficulty presets. For players who want to tune the grind up or down without touching content.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images. Ships 9 C# source files (`Plugin`, `DifficultyProfiles`, `SkillSynergies`, `SkillConfigManager`, `GlobalUsing`, and `Patcher/{GameLoadPatch, MorningBonusPatch, AreaFamiliarityPatch, AreaFamiliarityService}`) — all 9 advertised features wired to live, re-verified code.

**Stability**: 10/10 — 0 CRITICAL, 0 DESIGN GAP, 0 genuine WARNING. The lone E16 "missing SimpEn.csv" is a documented false positive for a card-less mod. The two log-hygiene warnings from `code-quality.md` (leftover `[MorningBonus][DIAG]` LogInfo; breadcrumbs at invisible LogDebug) were both **fixed 2026-08-24** but sit **uncommitted** in the working tree — commit them. Passes `/audit-mod`, `/critical-analysis` (SOLID), and `/code-quality` (10/10).

**Open work**: None blocking. Zero open retrospectives for SSB/SkillSpeedBoost/`crispywhips.skill_speed_boost`. Three non-blocking items: (1) commit the 2026-08-24 log-hygiene fixes; (2) the v1.9.5 Morning Bonus clock-hour fix is decomp-derived, builds clean, but has never been exercised in a live session; (3) README.md Version History + CHANGELOG.md lag the 1.9.7 version string (cosmetic).

**Framework compliance**: Tier 2 — heavy, correct adoption. Both hot-path coroutine postfixes (`ChangeStatValue`, `ActionRoutine`) use the CLAUDE.md single-composition IEnumerator-wrap pattern and the framework's `Api.Reflect` / `Api.StatAccess` / `Api.CardUtil` helpers (35 call sites across the 3 patch files); `[BepInDependency(..., SoftDependency)]` present, `ApplyPatch(harmony)` per-class in try/catch, `OnDestroy`→`UnpatchSelf()`. No deprecated patterns (`DropCollectionGuardPatch`, unfiltered hot-path prefixes, manual perk/blueprint injection, `ModLoaderVerison`). No further Tier-2 migration owed.

---

## Phase 0: Stabilize  *(audit score ≥ 8 and no open retrospectives — mostly skippable)*

> Nothing here breaks the mod. One housekeeping item only.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Commit the 2026-08-24 log-hygiene fixes (DIAG LogInfo removed, 3 breadcrumbs LogDebug→LogWarning; `MorningBonusPatch.cs`, `AreaFamiliarityPatch.cs`) | Housekeeping | P0 | Quick |

---

## Phase 1: Foundation

> Table-stakes hygiene. All Quick.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Add a `[1.9.7]` entry to CHANGELOG.md and a matching README.md Version History row (note it is a version-string-only bump) | Docs hygiene | P1 | Quick |
| Live-session verify the Morning Bonus 05:00–09:00 window (enable `MorningBonusEnabled`, gain XP at ~06:00 and ~11:00; confirm bonus only fires in-window) | Runtime verification | P1 | Quick |
| Refresh ROADMAP.md "Version at time of writing" on each release (was stale at 1.9.5 before this pass) | Docs hygiene | P1 | Quick |
| Optional: fold a latched `LogWarning` into the last silent early-return on the `SetCurrentValue` write path (`MorningBonusPatch.cs`) | Log hygiene | P2 | Quick |

*Localization: N/A — card-less utility mod; all player-facing text is BepInEx config-entry descriptions, not CSV-localized CardData. Do NOT create a dummy `SimpCn.csv`/`SimpEn.csv`.*

---

## Phase 2: Core Expansion

> The most impactful config-depth additions. Each composes as one more `multiplier *= …` line in the single `MorningBonusPatch.ChangeStat_Post` composition point (iterator postfixes can't compose — see Guardrails). All Near-Term in `Documentation/Ideas/SkillSpeedBoost/IDEAS.md`.

### Extend Difficulty Profiles to the post-v1.7 knobs + "Immersive" preset
**What**: `DifficultyProfiles.ApplyProfileSettings` currently writes through only `SkillExpMultiplier` + `EnableSkillStaleness`. Add write-throughs for `AreaFamiliarityEnabled` / `LevelScalingEnabled` / `MorningBonusEnabled` / `EnableSkillSynergies`, plus a new "Immersive" preset.
**Why**: Closes the "one key sets everything" half-truth — the advertised presets today touch 2 of the mod's 8 knobs. Profile plumbing + `SettingChanged` fire path already exist; added write-through lines only, no new hook.
**Requires**: none.
**Complexity**: Quick–Medium.

### Effective-Settings Summary Log (QoL)
**What**: On load, emit one config-gated `LogInfo` line per skill from the existing `GameLoadPatch.LoadMainGameData_Postfix` scan showing the resolved stack (global × per-skill multiplier, staleness on/off + rate). Config `LogEffectiveSettings` (default false).
**Why**: Directly answers README's #1 troubleshooting entry ("changes didn't apply" / "per-skill settings not working") — a player can confirm their config took effect without a decompiler. Pure logging; cannot regress balance.
**Requires**: none.
**Complexity**: Quick.

### `MaxComposedMultiplier` clamp + diagnostic warn
**What**: Clamp the multiplicatively-stacked total right before `if (multiplier <= 1f) yield break;` in `ChangeStat_Post`, with a config-gated warn when the cap is hit. Config `MaxComposedMultiplier` (default 0 = uncapped).
**Why**: Morning × per-skill × familiarity × synergy × level-scaling stack with no visibility; a maxed config can silently exceed 30x. Default keeps current behavior exactly.
**Requires**: none.
**Complexity**: Quick.

---

## Phase 3: Integration & Depth

> Cross-mod hooks and richer progression. All soft-dependency only — SSB stays fully functional without any partner mod.

### `SkillXpModifierService` — single public extension point *(design decision first)*
**What**: One registration surface — `SkillXpModifierService.Register(Func<string skillName, float multiplier> provider)` — folded into `ChangeStat_Post`'s composed multiplier as one more loop. Supersedes the informal "promote `SkillSynergies` to public" idea.
**Why**: Every cross-mod idea below currently wants a bespoke public setter. This turns each into a consumer-side change with zero further SSB edits.
**Requires**: decisions on provider ordering/cap interaction, per-tick delegate cost vs. action-start caching, and API-stability commitment.
**Complexity**: Medium.

### Concrete cross-mod consumers (after the service lands)
**What**: RepeatAction — verify multipliers fire on RA-driven repeats + optional `ExcludeAutomatedFromSynergy` to stop AFK combo-cheesing; H&F "Focus Tonic" consumable → timed global XP flag; ACT/WDI/CMC station-anchored themed XP bonus via `AreaFamiliarityPatch.CurrentLocationUid`; MUM ship/swap external Difficulty-Profile "balance packs"; a future combat mod chaining kills into `SkillSynergies`.
**Why**: Mutual benefit; SSB becomes the fleet's progression-tuning backbone.
**Requires**: `SkillXpModifierService` above; coordination with each partner mod's version.
**Complexity**: Medium (mostly consumer-side).

### Condition- and time-based bonus knobs
**What**: Well-Rested / Well-Fed XP bonus (reads player condition stat via `GameManager.Instance`, mirrors morning-bonus path); its inverse `DisableXpGainWhileStarving` low-condition suppression; Daily First-Use "warm-up" bonus; Level Scaling curve toggle (Linear/EaseIn/EaseOut); Night-Owl `IsNightWindow()`.
**Why**: Deepens the "tune the grind" identity and gives Hardcore/Immersive profiles both carrots and sticks.
**Requires**: Night-Owl is **blocked** on the Phase 1 Morning-window runtime verification (shared clock math); the rest are independent.
**Complexity**: Quick–Medium each.

---

## Phase 4: Polish

> N/A for art — 0 CardData / 0 custom images / 0 GIF candidates. Polish here is docs and log accuracy.

| Item | What | Complexity |
|------|------|------------|
| Docs sync pass | Keep README.md Version History + CHANGELOG.md in lockstep with the version string on every bump | Quick |
| Description accuracy | Re-confirm ModInfo.json Description enumerates only shipped features after each Phase 2/3 addition | Quick |

---

## Long-term Vision

> Where Skill Speed Boost should be at v2.0.

SSB's natural endpoint is "the fleet's progression-tuning backbone": a single, generic `SkillXpModifierService` any other mod (or a future combat/companion system) registers an XP-modifier provider against, plus a complete, honest set of difficulty presets that move every knob. It stays a zero-content, save-safe utility — the value is breadth and composability of tuning knobs, never CardData. The biggest addition that only becomes justified at that scale is a small optional persistence file (for streak / rested-XP-bank / familiarity-decay ideas that today can't survive a reload), gated behind an explicit opt-in so the "no save-file modifications" guarantee holds by default.

**Potential major additions** (not yet justified — revisit after Phase 3):
- `SkillXpModifierService` public API + first real consumers — turns SSB into an extensible platform rather than a closed tuner.
- Optional persistence layer (streak bonus, rested-XP bank, area-familiarity decay) — unlocks the whole "stateful progression" idea cluster that in-memory-only design currently blocks.
- Seasonal / biome / tool-quality XP weighting — each needs a confirmed data hook (`GameQuery.CurrentSeason` null-risk; tool capture at the paired `ActionRoutine` hook) before design.

These live in `Documentation/Ideas/SkillSpeedBoost/IDEAS.md` (Medium-Term / Long-Term sections) — already spec'd.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new config/feature phase | Run `/audit-mod SkillSpeedBoost` (+ `/code-quality`) and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log`; refresh `lib/Assembly-CSharp.dll` if the mod makes direct typed game calls |
| After the Morning-window live check | Update W3 status here and in `.audit/summary.md`; unblock Night-Owl if it passes |
| Before each public release | Sync ModInfo/Plugin.cs/README/CHANGELOG version strings; `/export-to-repo SkillSpeedBoost` |

---

## Skill Cheatsheet for This Mod

```
/audit-mod SkillSpeedBoost         — full health check, updates .audit/
/code-quality SkillSpeedBoost      — C# reliability/maintainability scan
/critical-analysis SkillSpeedBoost — adversarial review
/build-mod SkillSpeedBoost         — build Release DLL
/deploy-mods SkillSpeedBoost       — build + deploy to game
/update-mod-version SkillSpeedBoost <ver> — bump version in all 3 files
/export-to-repo SkillSpeedBoost    — push to public repo
```
