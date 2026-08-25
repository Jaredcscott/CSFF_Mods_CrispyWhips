# Roadmap: CSFF Mod Framework
Version at time of writing: 2.25.11
Date: 2026-08-24
Audit score: 10/10 (release-ready) - consolidated 2026-08-24; critical-analysis SOLID (2026-08-24),
code-quality 10/10 (2026-08-24, the one standing Warning M8 fixed same day)

## Current State

**Theme**: The single standalone engine every in-house CSFF mod depends on - mod discovery, JSON data
loading, WarpData resolution, sprite/audio/GIF loading, localization, and a large family of declarative
injectors (blueprint tabs, perks, smelting/drop/improvement/trading-value injection, WorldMap nodes,
the cross-mod Portal Hub, the declarative Animal Modding System, flavour-synergy pairs, standalone
GameModifierPackage auto-apply) plus Tier 1/2/3 `Api.*` helper surfaces. Content mods write C# only for
mod-specific logic.

**Content**: 1 item (Portal Kit) / 1 blueprint / 1 placed structure + 1 hub-exit helper / 1 perk
(Wayfinder) / 2 custom images - all bundled Portal Hub support fixtures, not player content. Engine
surface: 140 `.cs` source files, 19 injectors, 16 Animal-system files.

**Stability**: 10/10 - 0 open CRITICAL, 0 DESIGN GAP, 0 open WARNING, 6 MINOR (boilerplate polish +
verification debt). Build 0 errors / 0 warnings; versions synced 2.25.11 x3; localization 13/13 EN with
clean Chinese parity; bin/Release 0 drift.

**Open work**: substantial verification debt (nothing broken, all shipped-but-unexercised):
- `portal-hub-env-overlap-2026-08-24` (P) - Portal Hub travel map-overlap/board-corruption fix v2.25.6, built clean, not re-verified in-game.
- `thicketpine-north-loadtimes-2026-08-24` (P) - vanilla-area slow-load, `LogTrackTiming` armed, needs a play session + fresh log.
- `portal-exit-no-feedback` (v2.25.8 MessagePopup) - unverified.
- `questinjector-blueprint-reset-risk` (Open Plan) - `QuestInjector` gated OFF since 2.17.0, root cause never diagnosed.
- `RETRO_CLOSURE_PLAN_2026-07-23` (Open Plan, abandoned-for-now) - `QuestInjector`/`ResealCondition`/`CharacterRosterInjector` closure; harness deleted 2026-07-27.
- Animal System M3-M6 in-game acceptance never run (traps/tracks/encounters/tame+companion).

**Framework compliance**: This IS the framework - it defines Tier 1/2/3. Code quality is the fleet
gold-standard reference (all patching programmatic, both transpilers transfer labels/blocks, 0 silent
catches, all spawn chains logged).

---

## Phase 0: Verify shipped-but-unexercised fixes  *(the real next action - nothing is broken, this is verification debt)*

> No stabilization work is outstanding (10/10). But a large body of additive fixes and the entire
> Animal M3-M6 surface are code-reviewed + adversarially confirmed yet never played. Convert this debt
> to confidence with focused play sessions + fresh logs.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Play-verify Portal Hub travel fix (v2.25.6) - outbound + Return to Portal, no map overlap / board corruption over multiple sessions | Retro close (`portal-hub-env-overlap`) | P0 | Medium |
| Play-verify portal-exit MessagePopup (v2.25.8) - blocked/failed exit surfaces a popup | Retro close (`portal-exit-no-feedback`) | P0 | Quick |
| Collect a fresh log for ThicketPine-north slow-load (`LogTrackTiming` already armed) - test `CheckForTracks` theory | Retro close (`thicketpine-north`) | P0 | Medium |
| Animal M3-M6 in-game acceptance - author a species manifest exercising traps/tracks/encounters/tame+companion; confirm save/load persistence | Verification (M7) | P0 | Complex |
| Play-verify the older map/gate backlog: `EnvKeyMatchesUid`, `SetBlueprintStage`, CardPresence gate-wide reseal, `RebuildPathfindingLookup`, outdoor-trigger gate | Verification (M6) | P0 | Medium |

---

## Phase 1: Foundation

> Table-stakes hygiene. Framework is already clean here - these are the small standing items.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Model the framework's own Portal Kit item + Wayfinder perk to full completeness (M1: 9 boilerplate fields; M2: `NoSafetyMode`) via `/repair-items` + `/edit-perk` | Reference-content polish | P1 | Quick |
| Cache the one uncached `AccessTools.Field` in `DropInjector` (M4) | Perf hygiene | P2 | Quick |
| Keep versions synced across ModInfo/Plugin.cs/README on every bump (currently 2.25.11 x3) | Version hygiene | P1 | Quick (ongoing) |

---

## Phase 2: Core Expansion (engine capability)

> The framework's "content" is engine capability. These are the highest-value seams to open next.

### Author-facing cookbook docs for the shipped-but-undocumented injectors
**What**: `CSFF_Patterns.md` cookbook entries for the Animal M3-M6 subsections (traplines, spoor tracks,
encounters, tame->companion), `NPCCharacterPerk` shared-chassis NPCs, `FlavourMatrix/*.json` synergy
pairs, and `Modifiers.json` standalone GameModifierPackage.
**Why**: several complete, framework-owned capabilities are unreachable by any author but the one who
wrote them - docs are the single highest-value-per-effort work in the file.
**Requires**: none.
**Complexity**: Medium.

### Ship a first real consumer for each unexercised injection path
**What**: land CMC Village Guards as the acceptance proof for `NPCCharacterPerk` + `ConnectionGates.LockConditions`;
play the CMC winter snow-drift `ResealCondition:TimerRegrowth` once winter arrives; exercise
`CharacterRosterInjector` via a scripted fixture.
**Why**: each of these is a shipped-but-never-run path; a first consumer turns "load-validated" into "verified."
**Requires**: Phase 0 verification cadence.
**Complexity**: Medium.

### Root-cause the QuestInjector blueprint-reset so its default-OFF gate can flip back on
**What**: run the single-variable graduation test (enable -> ship a quest -> save -> reload); if the
reset recurs, look next to `ForeignInstanceReconciler` (blueprint UID instance-identity re-split).
**Why**: every quest-chain idea mod is dead behind the 2.17.0 default-OFF gate.
**Requires**: read `questinjector-blueprint-reset-risk.md` in full; re-author `RETRO_CLOSURE_PLAN` Phase 0 (harness deleted 2026-07-27).
**Complexity**: Complex.

---

## Phase 3: Integration & Depth

> Generalize hardcoded subsystems and build the highest-risk test infrastructure.

### Generalize `WildlifeRaidService` -> `Api.Raid` / `Raids.json`
**What**: keep the engine (day-rollover roll, container scan, sealed-container exemption, dedup gate) in
the framework; expose rule values (trigger encounter, target tag, effect) via a registration seam.
**Why**: fully shipped but locked to one rule (bear -> spoil `tag_NotSafeFromAnimals`); Sirus23 is the first consumer.
**Requires**: none.
**Complexity**: Medium.

### Build the save-compat test harness
**What**: scripted save fixtures for the add-content -> save -> remove-mod -> load matrix.
**Why**: the highest-risk gap - `QuestInjector`/`CharacterRosterInjector`/`ResealCondition`/Animal
persistence all need it, and the prior disposable fixtures were deleted 2026-07-27.
**Requires**: none.
**Complexity**: Complex.

### Animal M4 feed duty (`AffectItems` action)
**What**: `AnimalValidator.cs:262` hard-rejects it today; decide whether feeding reuses `Api.Inventory.Consume`
+ the lifecycle ticker and whether it produces anything (dung, wool growth) or only resets satiation.
**Why**: next concrete milestone of an already-shipped subsystem.
**Requires**: Animal M3-M6 acceptance (Phase 0).
**Complexity**: Medium.

---

## Phase 4: Polish

> Author-time validation and the small robustness tail.

| Item | What | Complexity |
|------|------|------------|
| Author-time gate-misconfig check | Fold the v2.17.1 runtime Warn (`HideTravelDA`+`RestoreDAOnUnlock:false`) and a `LockConditions`/`GateConditions` conflict check into the WorldMap export validators | Medium |
| `LocalTickCounter` end-to-end usability | Confirm a card can receive ticks by registration alone; if so add a cookbook line, else add an attach surface | Medium |
| `ProcessAllService` | Provide one shared Grind All / Hammer All / Blast All shape (now unblocked - ActionRouter + Inventory + SpawnService shipped); ACT + WDI consume | Complex |

---

## Long-term Vision

> Where the framework should be at v3.0.

The framework is already the fleet's mature, gold-standard engine; its next major arc is **verification
infrastructure and authoring reach**, not new subsystems. v3.0's natural endpoint: a save-compat test
harness that lets every injection path graduate from "load-validated" to "verified" without a manual
play session, a complete `CSFF_Patterns.md` cookbook so external authors can reach every shipped
capability, and the last hardcoded subsystems (`WildlifeRaidService`, single-rule paths) generalized to
declarative `*.json` + `Api.*` registration seams. The QuestInjector root-cause is the one gate whose
resolution unblocks a whole class of downstream idea mods.

**Potential major additions** (not yet justified - revisit after Phase 3):
- `Api.ModState` save-persistent helper - a first-class per-mod save blob so mods stop hand-rolling hidden GameStats.
- `ActionInjections.json` / `RecipeInjections.json` generalization - declarative action/recipe grafting onto vanilla cards.
- Player-facing "pick any GameModifierPackage" character-creation UI (explicitly deferred as content/UX, not engine).

These live in `Documentation/Ideas/CSFFModFramework/IDEAS.md`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new engine capability | Run `/audit-mod CSFFModFramework` + `/code-quality CSFFModFramework` and update this roadmap |
| Game version update | Refresh EVERY mod's `lib/Assembly-CSharp.dll` (+ regenerate nstrip variants), rebuild all, run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis CSFFModFramework` to verify the fix |
| After a verification play session | Update the matching retrospective in `Documentation/Retrospectives/INDEX.md`; graduate on confirmation |
| Before a fleet suite release | `/package-mod-suite` (framework first, MUM last) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod CSFFModFramework          - full health check, updates .audit/
/critical-analysis CSFFModFramework  - adversarial review
/code-quality CSFFModFramework       - C# reliability scan (this mod's core surface)
/build-mod CSFFModFramework          - build Release DLL
/deploy-mods -CSFFMFW                - build + deploy framework to game
/diagnose-log                        - parse a BepInEx/Player log after a verification session
/package-mod-suite                   - rebuild suite, re-embed in MUM, bump MUM version
```
