# Roadmap: CSFF Mod Framework
Version at time of writing: 2.25.3
Date: 2026-08-23
Audit score: 10/10 (release-ready) — consolidated 2026-08-23, critical-analysis SOLID, code-quality 9/10 standalone (1 new low-severity finding, below canonical score threshold)

## Current State

**Theme**: The single standalone engine every in-house CSFF mod depends on — mod discovery, JSON data loading, WarpData resolution, sprite/audio/GIF loading, localization, and a large family of declarative injectors (blueprint tabs, perks, smelting/drop/improvement/trading-value injection, WorldMap nodes, the cross-mod Portal Hub, the declarative Animal Modding System) plus Tier 1/2/3 `Api.*` helper surfaces. Mods only write C# for mod-specific logic.

**Content**: 1 item / 1 blueprint / 2 structures / 1 perk / 2 custom images — the bundled Portal Hub kit (`CardData/Hub/`). This is engine-support content, not a content mod; the framework's real "surface" is its injectors and `Api.*` classes.

**Since 2.23.2**: the Animal Modding System (M3–M6) shipped and passed its own critical-analysis (2026-08-19); `SealableGateService` gained the v2.25.3 MultiHit durability-epsilon fix (confirmed in-game 2026-08-22, fleet-wide fix for ACT/CMC softlocked gates) plus a NEW 1-second poll-driven self-healing backstop (reviewed 2026-08-23, SOLID — one low-severity D4 finding: `FindCardOnPlayerBoard` is a first-match resolver, not urgent given today's singleton-in-practice usage); `TrapIntegrator` gained a small `NOT_USED_` orphaned-vanilla-card exclusion.

**Stability**: 10/10 — 0 CRITICAL, 0 DESIGN GAP. `critical-analysis` re-ran 2026-08-23 (SOLID, full diff review since 08-19). `code-quality` re-ran 2026-08-23 covering the Animal System + the SealableGateService/TrapIntegrator delta (closing a coverage gap open since 08-13) — 1 new low-severity Warning (M8, see Phase 1). Build clean (0/0), versions synced 2.25.3×3, EN 13/13 + Chinese parity CLEAN, bin/Release 0 drift.

**Open work**: Two 🟡 framework retrospectives — `questinjector-blueprint-reset-risk` (QuestInjector hard-gated OFF since 2.17.0 after the CMC 1.7.0 blueprint-reset; root cause never diagnosed) and `RETRO_CLOSURE_PLAN_2026-07-23` (3 shipped-but-never-exercised paths; Phase 0 reverted as a safety cleanup 2026-07-27). Both self-disclosed and mitigated — nothing armed, no live risk.

**Framework compliance**: This mod DEFINES the Tier 1/2/3 API surface (`Api.Reflect`, `Api.ActionRouter`, `Api.SpawnService`, `Api.TickEvents`, `Api.EncounterGuards`, `Api.CardFinder`, `Api.StatAccess`, `Api.RecipeInjector`, `Api.BlueprintAlternates`, etc.). All are honestly documented; the one unused API (`Api.ContainerSort`) is disclosed as unused rather than overclaimed.

---

## Phase 0: Stabilize  *(skipped — audit score 10/10, no framework-blocking open retrospectives)*

No CRITICAL issues and no armed risk. The two open framework retrospectives are verification-debt / gated-OFF, not stability blockers. Nothing to stabilize before new work lands.

---

## Phase 1: Foundation — close the verification debt

> The single most valuable open action. Version/localization hygiene is already green; the gap is *in-game exercise* of five shipped, code-reviewed, adversarially-confirmed fixes that no human has yet played.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| In-game verify CardPresence gate-wide reseal (v2.23.2) — dig open a portal-path gate, cross, reload → no softlock, gate stays open | Verification | P1 | Medium |
| In-game verify `SetBlueprintStage` connection-gate hook (v2.23.0) — build CMC River Bridge → East travel to Village Path unlocks (closes the 🔴 river-bridge retro's stated root cause) | Verification | P1 | Medium |
| In-game verify `EnvKeyMatchesUid` post-leave env-key match (v2.22.1) — River Bridge unlock + dug-open-then-reload SealableGate marker read | Verification | P1 | Medium |
| In-game verify `RebuildPathfindingLookup` (v2.23.1) — path across a freshly-injected clone node uses fresh edges, not stale `MapDict` | Verification | P1 | Medium |
| In-game verify outdoor-trigger gate (v2.22.2) — a wild-animal outdoor trigger no longer fires in a cave/interior and still fires outdoors | Verification | P1 | Quick |
| In-game verify the new `SealableGateService` MultiHit poll backstop (v2.25.3) — a gate left at near-zero durability with no further player action should open within ~1s and log `cleared (poll)` | Verification | P1 | Quick |
| Add an export-gate check greppping README Game Version against `CURRENT_VERSION.txt` so the doc doesn't drift stale on the next game-data bump | Tooling | P2 | Quick |
| M8 (code-quality, 2026-08-23): harden `SealableGateService.FindCardOnPlayerBoard` to reconcile ALL matching instances, not just the first, if a MultiHit challenge-card UID is ever duplicated | Robustness | P3 | Quick — not urgent, no reproduced bug |

All six verification items can be batched into one play session; sequence them via `/playthrough-test-plan`.

---

## Phase 2: Core Expansion — activate the loaded-but-dormant types

> The framework loads and registers several ScriptableObject types (since 2.1.0) that have no activation/injection surface yet. These are the highest-leverage engine additions because each unblocks a named downstream/idea mod.

### FlavourTag / CookingRecipeGroup / BookmarkGroup thin injectors
**What**: thin injectors mirroring `PerkInjector`/`BlueprintInjector` for the small dormant types. `FlavourTag` is near-trivial (SpiceTag parity, 2-field schema).
**Why**: they load + register but never inject — dead capacity today. `CookingRecipeGroup` unblocks CookingExpanded / DairyWorkshop / Brewery idea mods.
**Requires**: none.
**Complexity**: Medium (FlavourTag Quick).

### ConstructionCardGroup injector
**What**: an injector for the 12 vanilla `ConstructionCardGroup` instances (loaded since 2.1.0, never activated).
**Why**: the only currently-loaded type with zero activation surface AND a named consumer (DecorationAndComfort idea mod's door/wall/room variants).
**Requires**: none.
**Complexity**: Medium.

### GameModifierPackage standalone activation (`Modifiers.json`)
**What**: a way to apply a difficulty/challenge package without needing a full `PlayerCharacter`.
**Why**: loads + is reachable via `EasyPackageWarpData`, but challenge mods can't apply one standalone today.
**Requires**: none.
**Complexity**: Medium.

---

## Phase 3: Integration & Depth — generalize shipped subsystems + the save-compat harness

### Save-compat test harness (highest-risk gap)
**What**: scripted save fixtures for the add → save → remove-mod → load matrix.
**Why**: `QuestInjector` (gated OFF), `SealableGates.ResealCondition` (first CMC consumer unplaytested), `CharacterRosterInjector` (no consumer ever), `NPCCharacterPerk` loading (no consumer) all lack a completed real-world exercise. A harness is the durable way to close them — and the route to root-causing the QuestInjector blueprint-reset so its default-OFF gate can flip back on.
**Requires**: re-author the disposable fixtures deleted 2026-07-27.
**Complexity**: Complex.

### ProcessAllService (Grind All / Hammer All / Blast All)
**What**: one framework shape for batch-processing actions.
**Why**: now unblocked (ActionRouter + Inventory + SpawnService all shipped); ACT + WDI are waiting consumers.
**Requires**: none (dependencies shipped).
**Complexity**: Medium.

### Generalize WildlifeRaidService → `Api.Raid` / `Raids.json`
**What**: keep the engine (roll, container scan, sealed exemption, dedup gate) in the framework; expose rule values declaratively.
**Why**: fully shipped but locked to one hardcoded rule (bear → spoil `tag_NotSafeFromAnimals`).
**Requires**: none.
**Complexity**: Medium.

### Animal M4 feed duty (`AffectItems` action)
**What**: the next milestone of the already-shipped Animal subsystem; `AnimalValidator.cs:262` hard-rejects it today.
**Why**: Sirus23 is the first/only animal-layer consumer and the natural driver.
**Requires**: Animal cookbook docs (Phase 4) to be reachable by authors.
**Complexity**: Medium. See `Documentation/Plans/CSFFModFramework/Animal_System_Plan.md` (M4).

---

## Phase 4: Polish — docs + own-content completeness

| Item | What | Complexity |
|------|------|------------|
| Animal subsystem cookbook | Add a `CSFF_Patterns.md` "Adding a Roaming Animal" section — code is done, but the subsystem is unreachable by external authors with no doc | Medium |
| Injector cookbook sections | "Adding a Spirit / Trader / Location / Quest Chain / Character / Shared-Chassis NPC Variant (`NPCCharacterPerk`)" entries for the shipped NPC/Quest/Character/Map injectors | Medium |
| Portal Kit item completeness (M1) | `/repair-items` — add the 9 omitted boilerplate fields so the framework's own gold-standard content models the completeness the audit skills enforce downstream (cosmetic, no runtime impact) | Quick |
| Wayfinder perk completeness (M2) | `/edit-perk` — add the `NoSafetyMode` field (defaults false; non-functional today) | Quick |
| DropInjector field cache (M4) | Cache the one uncached `AccessTools.Field` lookup (load-time only) | Quick |
| Author-time gate-misconfig check | Fold the v2.17.1 runtime Warn (`HideTravelDA:true` + `RestoreDAOnUnlock:false`) and a `LockConditions`/`GateConditions` mutual-exclusion check into the F21–F27 WorldMap validators so they block export instead of surfacing in a player save | Medium |

---

## Long-term Vision

> Where the framework should be at v3.0.

At v3.0 the framework closes its "loaded-but-dormant" gap entirely — every ScriptableObject type it registers has an activation/injection surface and a `CSFF_Patterns.md` cookbook entry, so an external author can reach every subsystem (Animals, NPCs, Quests, Characters, Cooking groups, Construction groups, challenge packages) declaratively without reading framework source. The verification debt is retired by a real save-compat harness that lets `QuestInjector` graduate from hard-gated-OFF back to supported. The remaining hardcoded subsystems (`WildlifeRaidService`, the single-rule raid) become declarative (`Raids.json`), and `ProcessAllService` lands as the shared Grind/Hammer/Blast-All shape all the industry mods want.

**Potential major additions** (not yet justified — revisit after Phase 3):
- `Api.ModState` save-persistent helper — a sanctioned way for mods to persist small state across save/load without the blueprint-reset risk that QuestInjector hit.
- `CompanionService` — generalize the Sirus23 companion follow/stay pattern into a framework surface (currently open-coded per mod).
- `ActionInjections.json` / `RecipeInjections.json` generalization + `GameSourceModify` nested-append — declarative reach for the last patterns that still require mod C#.

These live in `Documentation/Ideas/CSFFModFramework/IDEAS.md` (full deferred-spec list) and the two Animal plan docs under `Documentation/Plans/CSFFModFramework/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new injector/API phase | Run `/audit-mod CSFFModFramework` and update this roadmap |
| Game version update | Refresh `lib/Assembly-CSharp.dll` from the live game, rebuild, run `/decompile-assembly` + `/extract-latest-carddata`, re-run `/diagnose-log` (framework lib is the fleet canonical — every content mod copies from it) |
| After fixing a critical issue | Run `/critical-analysis CSFFModFramework` to verify the fix |
| After each Tier/injector addition | Add the matching `CSFF_Patterns.md` cookbook entry in the SAME commit — a shipped injector with no doc is unreachable |
| Before re-enabling QuestInjector | Run the single-variable graduation test on a disposable save (does blueprint research survive save/reload?) per `questinjector-blueprint-reset-risk.md` — never ship it enabled without this |

---

## Skill Cheatsheet for This Mod

```
/audit-mod CSFFModFramework          — full health check, updates .audit/
/critical-analysis CSFFModFramework  — adversarial review
/consolidate-audit CSFFModFramework  — merge sub-audits → summary/ideas/ROADMAP/plan
/playthrough-test-plan               — sequence the Phase 1 in-game verification batch
/build-mod CSFFModFramework          — build Release DLL
/deploy-mods -CSFFMFW                — build + deploy framework to game (deploy FIRST in any batch)
/repair-items CSFFModFramework       — auto-fix the Portal Kit boilerplate (M1)
/export-to-repo CSFFModFramework     — push to public repo
```
