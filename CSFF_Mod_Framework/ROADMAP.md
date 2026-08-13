# Roadmap: CSFF Mod Framework
Version at time of writing: 2.22.1
Date: 2026-08-12
Audit score: 9/10 — PASS (0 CRITICAL, 0 DESIGN GAP, 1 WARNING — documentation-only; consolidated 2026-08-12)

## Current State

**Theme**: The engine layer for every in-house CSFF mod — mod discovery, JSON/data loading, WarpData resolution (incl. nested arrays and non-UID SO bodies), sprite/audio/GIF/localization loading, and a large family of declarative injectors (blueprint tabs, perks, `NPCCharacterPerk`, smelting, drops, environment improvements, trading values, WorldMap nodes + connection gates, Portal Hub, STAs, NPC agents). Content mods only write C# for genuinely mod-specific logic.

**Content** (the framework's own minimal built-in demo set): 1 item (Portal Kit `csffmfw_portal_kit`, CT0) / 1 blueprint (`Bp_PortalKit`, CT7) / 2 placed structures (Portal Hub `csffmfwportalplaced` CT2 + exit card `csffmfw_hub_exit`) / 1 perk (Arcane Wayfinder `csffmfw_perk_wayfinder`) / 2 custom images (`Portal_Kit`, `Portal_Placed`). Deliberately tiny — the framework is engine code (~132 `.cs` files), not content. Every card has custom art; no art gaps.

**Stability**: 9/10 — PASS. Zero open CRITICAL, zero design gaps, acquisition coverage clean (0 unreachable / 0 dead-end). Build clean (0 errors / 0 warnings), versions synced **2.22.1×3**, bin/Release sync 0 drift, localization CLEAN including Chinese parity (13/13 EN + 13/13 CN). Code-quality 10/10 (re-run 2026-08-12) and critical-analysis **SOLID (re-run 2026-08-12 against v2.22.1)**. The single point off is verification/documentation debt (see Open work), not correctness debt — the framework is exportable. The one open WARNING is developer-doc honesty (README Game Version line), not a functional defect.

**Open work**:
- **M6 (verify the v2.22.1 connection-gate fix, actionable — adversarial DONE, in-game pending)** — v2.22.1 (committed 2026-08-12) added `CardUtil.EnvKeyMatchesUid` and routed `IsImprovementBuilt`/`MarkImprovementBuilt`/`SealableGateService.IsTargetEnvEntry` through it, fixing the names-annotated `DictionaryKey` mismatch that left a hand-built River Bridge from unlocking the CMC River Clearing → Village Path connection and made SealableGates marker *reads* miss save-loaded entries (memory `reference_envdictkey_names_annotated_matching`). code-quality re-ran clean 2026-08-12 **and `/critical-analysis` re-ran 2026-08-12 against v2.22.1, confirming the delta SOLID / additive-only (cannot regress).** Remaining: **in-game** verify the River Bridge East unlock and a dug-open-then-reload SealableGate marker read — the one change with no in-game pass.
- **W1 (README "Game Version" line stale again, actionable)** — `README.md:8` reads `EA 0.66d`; the repo advanced the game-data target to **EA 0.66f** on 2026-08-12 (`VanillaIds.json` `EA_0-66d`→`EA_0-66f`, commit `8201b449d` — the same v2.22.1 commit that touched README). Per CLAUDE.md §Docs-Honesty, a commit changing a compat claim should update the claim in the same commit. Developer-doc honesty only; zero runtime/player impact.
- **M3 (README In-House Mods table, actionable)** — all 9 sibling versions stale (e.g. CMC shown `1.10.1`, actual ~`1.46.x`; Repeat Action `1.6.2`, actual `2.0.1`; MUM `2.1.2`, actual `2.1.16`). Informational cross-reference only. Bundle with W1.
- **M7 (developer game-data path stale, actionable)** — `README.md:157-158` hardcode `Documentation/GameData/CSFF-JsonData_EA_0-65/`; CLAUDE.md §Key File Locations mandates the stable `CSFF-JsonData_Current/` alias. Bundle with W1.
- **In-game EA 0.66 verification (unrecorded)** — the framework builds clean against the real EA 0.66 assembly, but Harmony patch-apply verification at game launch is not yet captured in any log artifact. (EA is at 0.66f per memory `project_game_version` — fold the launch check into the next play session.)
- (RESOLVED / dropped) **W (Hub card WarpData compliance)** — **false positive**; re-verified 2026-08-12 that `csffmfw_portal_kit.json:16` and `csffmfwportalplaced.json:16` (and `Bp_PortalKit.json:92`) all carry `CardImageWarpType: 3`. No longer a Phase 1 item. **Chinese `SimpCn.csv`** — 13/13, parity CLEAN.
- (Y) `questinjector-blueprint-reset-risk` — CMC 1.7.0 blueprint-research reset; root cause undiagnosed. Mitigated: `QuestInjector` hard-gated OFF by default (`EnableQuestInjection=false`). Not release-blocking, but blocks every quest-chain idea mod.
- (Y) `RETRO_CLOSURE_PLAN_2026-07-23` — plan to field-exercise `SealableGates.ResealCondition` + `CharacterRosterInjector` never resumed; disposable harness fixtures deleted 2026-07-27.
- **Shipped-but-unexercised injection paths** — `SealableGates.ResealCondition` (first CMC consumer landed via village winter snow-drift 1.35.0, unplaytested), `CharacterRosterInjector` (no consumer), `NPCCharacterPerk` loading (new 2.20.0, no consumer).

**Framework compliance**: N/A in the usual sense — this **is** the framework. It *provides* Tier 1 (`Api.Reflect`/`Collections`/`Inventory`/`Gate`/`VanillaIds`/`CardUtil`/`GameQuery`), Tier 2 (`Api.ActionRouter`/`SpawnService`/`TickEvents`/`EncounterGuards`/`ContentModPlugin`), and Tier 3 (`Api.CardFinder`/`StatAccess`/`RecipeInjector`/`ContainerSort`/`BlueprintAlternates`). Code-quality is 10/10: no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no `ModLoaderVerison`/`ModEditorVersion`, all normalizers mod-prefix-filtered, `GameManager` resolution routed through `ReflectionCache.FindTypeInAssemblyCSharp`.

---

## Phase 0: Stabilize  *(SKIP — audit score 9/10 ≥ 8, and both open retros are yellow/Open Plan, not red)*

Nothing release-blocking. The framework is exportable at 9/10. The one item worth doing before advertising the v2.22.1 gate fix further is the M6 **in-game** verification (a single playtest — the adversarial `/critical-analysis` half is already done), which lands in Phase 1.

---

## Phase 1: Foundation

> Table-stakes hygiene + the one just-landed change that needs an in-game pass. All low-cost, all reduce future verification/documentation debt.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **W1 + M3 + M7 — one README doc pass** — Game Version → EA 0.66f (with the 7/9 NStrip-staleness caveat); refresh the In-House Mods sibling-version table (all 9 stale); swap the hardcoded game-data path (README.md:157-158) to the `CSFF-JsonData_Current/` alias | Docs hygiene / honesty | P1 | Quick |
| **M6** — in-game verify the CMC River Bridge → Village Path unlock and a dug-open-then-reload SealableGate marker read (the adversarial `/critical-analysis` re-run is DONE 2026-08-12, SOLID) | Runtime verify | P1 | Quick–Medium |
| In-game EA-version verification — launch, check `LogOutput.log`/`Player.log` for `HarmonyLib` patch-apply exceptions / `MissingMethodException` during `Awake`; confirm the normal ~10–15 Info-line summary (EA at 0.66f) | Runtime verify | P1 | Quick |
| **M2** — add `NoSafetyMode` to `csffmfw_perk_wayfinder.json` (cosmetic — do next time the file is touched) | Polish | P2 | Quick |
| **M1** — optional: `/repair-items` to add 11 boilerplate fields to Portal Kit (no runtime impact) | Polish | P3 | Quick |

> **Resolved since the 2026-08-11 roadmap** (no longer Phase 1 items): **W (Hub `CardImageWarpType: 3`** — confirmed already present, false positive); **W2 (README Game Version → EA 0.66d, fixed 2026-08-11 — but see W1: stale again at 0.66f)**; Chinese `SimpCn.csv` (13/13, parity CLEAN); bin/Release deploy hygiene (0 drift); D16 (`WaitForSeconds` → `WaitForSecondsRealtime`), D17 breadcrumb pass, A4 (`GameManager` resolution), G4 (CardUtil catches) — all landed in earlier fix passes.

---

## Phase 2: Core Expansion

> Engine features that unblock the most downstream mods. The framework's "content" is its API surface.

### Activate the loaded-but-dormant SO types
**What**: thin injectors for `FlavourTag`, `CookingRecipeGroup`, `BookmarkGroup`, `ConstructionCardGroup`, `GameModifierPackage` — all load+register since 2.1.0 but have no activation/injection surface.
**Why**: FlavourTag = trivial SpiceTag parity (2-field schema); `ConstructionCardGroup` (12 vanilla instances) has a named consumer (DecorationAndComfort idea mod); `CookingRecipeGroup` lets station mods register in the cooking UI properly. Closes the gap-audit Phase 1 tail.
**Requires**: none (mirror `PerkInjector`/`BlueprintInjector`).
**Complexity**: Medium (per type; FlavourTag is Quick).

### First consumer + cookbook doc for `NPCCharacterPerk` (shipped 2.20.0)
**What**: land CMC Village Guards as the acceptance proof (`NPCCharacterPerk/*.json`), then add a `CSFF_Patterns.md` "Adding a Shared-Chassis NPC Variant" cookbook entry.
**Why**: 2.20.0 shipped the loader but no mod exercises it — a fresh shipped-but-unexercised path. The engine is only proven when a real consumer ships + playtests.
**Requires**: CMC Village Guards build (master plan section 10.8).
**Complexity**: Medium.

### Author-facing schema docs for the shipped Animal subsystem (`Animals/*.json`, v1)
**What**: `CSFF_Patterns.md` cookbook entry — the code is done (`AnimalService`/`AnimalLoader`/`DutyBuilder`/lifecycle ticker) but has zero authoring guide, so it's unreachable by external authors. (Concrete engine-side next milestone is Animal M4 feed duty — see `Documentation/Plans/CSFFModFramework/Animal_System_Completion_Plan.md`.)
**Why**: highest-value docs gap — a fully-shipped subsystem no one outside the repo can use.
**Requires**: none.
**Complexity**: Medium.

---

## Phase 3: Integration & Depth

> Cross-mod hooks and generalizations that retire duplicated per-mod C#.

### `ProcessAllService` (Grind All / Hammer All / Blast All)
**What**: one declarative shape (or thin `Api` registration) for "process all inventory" buttons shared across ACT + WDI.
**Why**: the centralization plan parked this until ActionRouter + Inventory + SpawnService landed — they now have, so it's unblocked.
**Requires**: coordination with ACT + WDI versions.
**Complexity**: Medium.

### Fleet adoption of `DropInjections.json` + `TradingValues.json`
**What**: H&F migrates ~400 LOC forage C# to `DropInjections.json` (CMC already did as the acceptance proof); each content mod ships a `TradingValues.json` price table (2.18.0; CMC table built, not yet deployed/tested).
**Why**: both are shipped engine features with partial adoption — closing the fleet loop retires duplicated code and fixes vanilla 0-cost NPC trades.
**Requires**: per-mod authoring passes.
**Complexity**: Medium (per mod).

### Playtest + author-time validation for connection gates (`LockConditions` 2.20.0/2.20.2; `EnvKeyMatchesUid` 2.22.1)
**What**: playtest CMC's `cmcStatVillageCrime` banishment gate (first `LockConditions` consumer) AND the v2.22.1 River Bridge East-unlock fix; add an author-time `/audit-environments` check for a `LockConditions`+`GateConditions` pair that can never both hold. Fold in the same author-time check for the `HideTravelDA:true`+`RestoreDAOnUnlock:false` red-X compass slot (v2.17.1 catches it only at runtime).
**Why**: the mid-run re-eval, `LockConditions`, and post-leave env-key match paths are only load-verified; catch misconfig at export instead of in a player save.
**Requires**: none.
**Complexity**: Medium.

### Root-cause QuestInjector so its default-OFF gate can flip back on
**What**: run the single-variable graduation test (enable → ship a quest → save → reload); if the reset recurs, the fix likely lives next to `ForeignInstanceReconciler`.
**Why**: every quest-chain idea mod is dead behind the gate.
**Requires**: read `questinjector-blueprint-reset-risk.md` in full first; re-author `RETRO_CLOSURE_PLAN` Phase 0 (harness deleted 2026-07-27).
**Complexity**: Complex.

---

## Phase 4: Polish

> Art, animation, text. Minimal for an engine mod.

| Item | What | Complexity |
|------|------|------------|
| Portal Hub GIF | Optional idle animation on the placed CT2 active state | Medium |
| Description pass | Spot-check the 4 Hub cards + Wayfinder perk descriptions against actual behavior after the 2.20.x/2.22.x gate changes | Quick |

*No missing/placeholder art — both custom cards (`Portal_Kit`, `Portal_Placed`) ship real PNGs. Chinese localization complete (13/13).*

---

## Long-term Vision

> Where the framework should be at v3.0.

The framework's endpoint is a **fully declarative content pipeline**: every vanilla-adjacent content type (animals, encounters, quests, characters, cooking recipes, construction groups, difficulty packages) authored purely in JSON with a matching `CSFF_Patterns.md` cookbook entry and an author-time validator, so a content-mod author never writes reflection C# for a supported type. Today ~half the injectors are shipped-but-undocumented or shipped-but-unexercised; closing that gap (docs + a save-compat test harness for the add → save → remove-mod → load matrix) is the single biggest robustness win. The generalization of one-rule subsystems (`WildlifeRaidService` → `Api.Raid`/`Raids.json`) and a save-persistent `Api.ModState` helper are the natural v3.0 additions once the current dormant-type + verification-debt backlog clears.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **Save-compat test harness** — scripted save fixtures so injection regressions (the QuestInjector class of bug) surface before ship, not in player saves. Highest-risk gap.
- **`Api.ModState`** — keyed save-persistent blob for mod state the card system can't hold (NPC trust, companion morale, world-hardship toggles).
- **Encounter activation + wiring** — Encounter loads since 2.1.0 but nothing exercises it; unblocks every spirit/hostile-encounter idea mod. Diagnostics-first.

These live in `Documentation/Ideas/CSFFModFramework/IDEAS.md` (already spec'd).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod CSFFModFramework` and update this roadmap |
| Game version update | Swap `lib/Assembly-CSharp.dll` for the new game binary FIRST, rebuild, diff every `[HarmonyPatch(typeof(...))]` target, run `/update-mod-version`, re-run `/diagnose-log` (the P4 lesson) |
| After fixing a critical issue | Run `/critical-analysis CSFFModFramework` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo CSFFModFramework` and bump minor version |
| Fleet EA-version bump | Every one of the other 9 mods ships its own independently-stale `lib/Assembly-CSharp.dll` — any with compile-time `[HarmonyPatch(typeof(...))]` attributes needs the same lib-swap-and-rebuild (7 of 9 use NStrip'd libs that must be regenerated externally, not copied) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod CSFFModFramework         — full health check, updates .audit/
/critical-analysis CSFFModFramework — adversarial review
/build-mod CSFFModFramework         — build Release DLL
/deploy-mods CSFFModFramework       — build + deploy to game (or Deploy-Mods.ps1 -CSFFMFW)
/update-mod-version CSFFModFramework <ver> — bump version in all files
/export-to-repo CSFFModFramework    — push to public repo
```
