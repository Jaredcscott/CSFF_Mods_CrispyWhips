# Roadmap: Water Driven Infrastructure
Version at time of writing: 1.6.0
Date: 2026-06-13
Audit score: 8/10

## Current State

**Theme**: Late-game, water-powered manufacturing and automation — build large-scale infrastructure (sawmill, forge, workshop, grinding mill, ore sluice, fishpond) near rivers, powered by water wheels and mill races, fed by a copper/iron metalworking pipeline. For players past the early survival tier who want bulk processing and a manufacturing loop. Hard-depends on AdvancedCopperTools.

**Content**: 18 items / 18 blueprints / 10 structures / 3 perks / 25 custom images (88 image refs, all resolve). 0 SelfTriggeredActions, 0 spawn Triggers.

**Stability**: 8/10 — 0 critical, 0 design gaps, 5 warnings (3 are design-intent decisions, 2 cosmetic). Critical Analysis verdict: SOLID (0/0/0/0).

**Open work**:
- `village-path-east-travel` 🟡 Pending — WorldMap integration (CMC Village Path ↔ WDI mill-race `Api.WorldMap` edge consumption). The 2-stage WorldMapInjector fix is deployed but not yet verified in-game. *Note: the audit's W3 references a non-existent `village-path-west-travel.md`; the real file is `-east-travel.md` and is Pending, not Open.*

**Framework compliance**: **Tier 1 adopted, Tier 2 not yet.** WDI uses `CardUtil` extensively (card identity, action names, inventory access) and consumes `Api.WorldMap` (1.6.0). It does **not** use any Tier 2 runtime service — `ActionInterceptPatch` still patches `ActionRoutine` + `PerformStackActionRoutine` directly (CLAUDE.md says register an `ActionRouter.ActionHandler` instead), `FishpondPopulationPatch` polls its own tick (should use `Api.TickEvents`), and the SD4=200 iron-bar init is a manual `GiveCard` postfix (should use `Api.SpawnService`). No deprecated/dangerous patterns: no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no `ModLoaderVerison`, all mutation filtered to `water_sawmill_*`.

---

## Phase 0: Stabilize

> Score is 8 with one Pending retro and one player-visible CN glitch. Light pass — close these before new content.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Verify `village-path-east-travel` in-game (travel to CMC Village Path; confirm mill-race network extends across the new node), then graduate or reopen the retro | Retro close | P0 | Medium (needs in-game test) |
| Fix leaked key in `Localization/SimpEn.csv:536` — CN value for `Water_Sawmill_Bp_IronAxle_UnlockConditionsDesc` reads `需要锻铁条Water_Sawmill_ForgePlaced_IncreaseTemperature_ActionName`; trim to `需要锻铁条` | Localization fix | P0 | Quick |
| Reconcile audit W3 — `.audit/summary.md` cites missing `village-path-west-travel.md`; the actual file is `-east-travel.md` (Pending). Update the audit note | Audit hygiene | P0 | Quick |

---

## Phase 1: Foundation

> Table-stakes hygiene plus the two low-risk Tier 2 migrations. Versions already match (ModInfo / Plugin.cs / README all read 1.6.0) — keep them that way.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **Decide forge iron-heating policy (audit W1).** `WaterDrivenForge_Placed.json` "Heat Metal Items" uses `FuelChange: 200`, which cannot overcome iron's −400/DTP drain below 1000°C (Workshop uses 400). Either declare the standalone forge **copper-only** and document it, or raise to `400.0`. Align README's "Smelt Iron Components" claim to match. | Bug/design | P1 | Quick |
| Add `InventorySlots: []` base field to the 4 iron items (`IronParts`, `IronBearing`, `IronAxle`, `IronWrench`) for consistency (audit W4) | Cosmetic | P1 | Quick |
| **Migrate `FishpondPopulationPatch` → `Api.TickEvents`.** Removes a mod-side per-tick poll; lower-risk first Tier 2 step. Verify fish growth + harvest still fire across day rollovers. | Framework Tier 2 | P1 | Medium |
| **Migrate SD4=200 iron-bar init → `Api.SpawnService`** (`Spawn(uid, statOverrides)` / `OnNextSpawn`) instead of the manual `GiveCard` postfix in `ActionInterceptPatch`. Verify smelted iron components still produce SD4=200 wrought iron bars that pass downstream iron gates. | Framework Tier 2 | P1 | Medium |
| Confirm `bin/Release/` is byte-fresh after any edit above (Critical Analysis S12 verified parity — keep it) | Build hygiene | P1 | Quick |

> Chinese localization baseline already exists (`SimpCn.csv`); only the one leaked value (Phase 0) needs fixing.

---

## Phase 2: Core Expansion

> The most impactful additions that extend the existing loop. All three are largely JSON/CSV and pre-specced in `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md`.

### Sawdust byproduct from Sawmill Cut
**What**: Add a `Sawdust` CT0 item as a secondary output of the sawmill **Cut** action (currently yields only 8 Planks). Usable as tinder / smoking fuel / fire-starter.
**Why**: Closes a thematic gap (sawing produces no waste product) and gives the sawmill a second reason to run. The Cut handler is already C#-intercepted, so this is one `GiveCard` line + 1 item JSON + CSV rows.
**Requires**: Verify no vanilla `Sawdust` UID exists before authoring a new one.
**Complexity**: Quick

### Millwright Perk (skill head-start)
**What**: A 4th character-creation perk granting a Construction-skill head-start (`StartingStatModifiers`, +75 toward the 150 cap) instead of items. Copy `Perk_ForgeStart.json`, drop `AddedCardsWarpData`, ~8 Moons, Situational group.
**Why**: All 3 shipped perks are item-granting; none addresses skill progression. Fills the progression-variety gap for players who want to *build* the chain rather than be handed a kit. (See memory `reference_perk_modifier_conventions`.)
**Requires**: none
**Complexity**: Quick

### Fish Funnel (Ideas "Phase 2")
**What**: River-placed CT10 EnvImprovement that boosts an adjacent vanilla `FunnelTrapLocation` fish-population rate (chain: `Bp_FishFunnel` → `FishFunnel_Kit` → `FishFunnel_Placed`). 3 JSON files + 11 CSV rows + 1 tab entry, fully specced in IDEAS.md.
**Why**: Extends WDI's water-and-fish theme into vanilla fishing infrastructure; a natural companion to the Fishpond. Try the pure-JSON `PassiveEffect` path first (Path A); fall back to a per-tick C# patch only if it doesn't fire.
**Requires**: Confirm which `SpecialDurability` index is the population counter by reading vanilla `FunnelTrapLocation.json` first.
**Complexity**: Medium

---

## Phase 3: Integration & Depth

> Cross-mod hooks, the larger Tier 2 refactor, and the multi-environment power network. For experienced players and long-term maintainability.

### ActionRouter migration (the big Tier 2 step)
**What**: Replace `ActionInterceptPatch`'s direct `ActionRoutine` / `PerformStackActionRoutine` patches with registered `Api.ActionRouter.ActionHandler`s for Cut, Blast, Hammer All, and the inventory-backed Workshop buttons.
**Why**: CLAUDE.md is explicit — the router owns two-tier identity, frame dedup, and the single IEnumerator wrap point. WDI currently reimplements all of that (the 1.5.0 "hammer dedup" fix exists precisely because two paths double-fired). Migrating deletes fragile mod-side code and future-proofs against framework changes to the wrapped coroutines.
**Requires**: Do it incrementally, one handler at a time, verifying each action in-game before the next. Highest regression risk in the mod — treat carefully.
**Complexity**: Complex (may span sessions)

### Mill Race Outlet Station Gating (Ideas "Pass 2–4")
**What**: Make the mill-race connectivity graph actually gate placement/use — outlets and stations only work where a complete water route exists; frozen outlets block draw. Full state model and `MillRaceNetwork.ShouldBlockAction` API shape are specced in IDEAS.md.
**Why**: Today the network connectivity is computed but barely enforced; this turns the mill-race chain from cosmetic into a real placement constraint, giving the whole "build out from the river" loop meaning.
**Requires**: Confirm the prerequisite static-map logs (`edges`/`patched`/`clones` counts) before coding; `village-path-east-travel` (Phase 0) validated first.
**Complexity**: Medium–Complex

### Irrigation Mill Race Chain (Ideas "Phase 3")
**What**: Multi-environment water plumbing — directional mill-race segments on Path cards carry flow between environments; a terminal Irrigation Mill Race auto-tops `TilledField`/`GardenPlot` Hydration when an unbroken chain back to water exists. 6 JSONs + 16 CSV rows + BFS in `IrrigationChainPatch.cs`.
**Why**: The single most ambitious extension of the core theme — turns WDI from "stations near a river" into a true power/water network. The mechanism can be built and tested against vanilla fields now.
**Requires**: H&F Phase 4 (crops) for meaningful field content; merge the per-tick hook with any Fish-Funnel/maintenance tick dispatcher to avoid double-hooking `IncrementGameTimeByOne`.
**Complexity**: Complex

### CMC — Iron Fishing Rod fittings (tightest cross-mod synergy)
**What**: Make `CMC_IronFishingRodFittings` (or equivalent) a WDI Workshop "Forge Iron Fittings" recipe, routing CMC's iron fishing-rod chain through WDI's metalworking loop; the finished rod then feeds WDI's Fishpond / Fish Funnel.
**Why**: WDI's Workshop is the repo's only iron-forging station, and CMC's rod needs iron fittings — a clean mutual fit.
**Requires**: Decide which mod **owns** the recipe to avoid a cross-mod output-UID silent no-op (memory `feedback_cross_mod_output_dependency`). Coordinate with CMC version.
**Complexity**: Medium

### Structure maintenance / wear loop
**What**: `UsageDurability` drain on water wheels / mill races plus a "Repair" DismantleAction consuming a few Planks.
**Why**: Infrastructure is currently permanent once built; an upkeep loop adds long-term engagement. *Decision needed:* decay rate, and whether a worn wheel stops powering downstream stations or just warns. Folds into the shared per-tick dispatcher.
**Requires**: per-tick hook (share with Irrigation/Fish-Funnel tick).
**Complexity**: Medium

### Lower-effort integration checks
- **RepeatAction bulk processing** — confirm WDI's IEnumerator-wrapped Cut/Hammer All/Smelt are RepeatAction-compatible so players can queue bulk runs; document the result. *Complexity: Quick–Medium.*
- **SheepHusbandry water trough** (coordinate with Sirus) — outlet-fed trough that hydrates enclosure animals; likely belongs in SH consuming a WDI water tag (memory `project_sheephusbandry_author`). *Complexity: Medium.*

---

## Phase 4: Polish

> Art, animation, and text. Custom art is already complete (88/88 refs resolve) — the wins here are animation and description accuracy.

| Item | What | Complexity |
|------|------|------------|
| Water Wheel GIF | Idle spinning-wheel animation on the placed Water Wheel — the mod's flagship visual; strongest GIF candidate (`Documentation/CSFF_GIF_Authoring.md`) | Medium |
| Forge / Workshop lit-state GIF | Animated fire/glow when the forge is hot (≥1100°) | Medium |
| Fishpond winter persistence (audit W2) | `Fishpond_Winter.json` thaw unconditionally reverts to `_filled`; a Stocked pond drops to Filled every spring. Add a conditional thaw branch to restore Stocked when population is high enough — or document the seasonal reset as intended | Quick (after design decision) |
| Description pass — WorkshopKit recycling (audit W5) | `WaterDrivenWorkshop_Kit.json` smelts via `ModType:2` → 1 MetalNugget while all other smeltables use `ModType:3` + multi-nugget `ProducedCards`. Confirm this is an intentional recycling deterrent (4150g mass) and note it in the description, or align to the standard pattern | Quick |
| Description pass — forge iron claim | Ensure README + card descriptions match the Phase 1 W1 decision (copper-only vs iron-capable forge) | Quick |

---

## Long-term Vision

> Where WDI should be at v2.0.

WDI becomes the repo's **automation and water-distribution backbone**: not just stations beside a river, but a genuine multi-environment power/water network — directional mill races carrying flow across the map, irrigation feeding crops, multiple stations sharing a wheel, and an upkeep loop that makes the infrastructure feel alive rather than permanent. It graduates from a content mod to an integration hub: the iron-forging station for CMC's tools, the water provider for SheepHusbandry's livestock and H&F's crops, and the bulk-processing layer RepeatAction queues against. The biggest addition not yet justified by current scope — the **multi-environment plumbing/power graph** (already specced as the Irrigation Chain) — becomes the v2.0 centerpiece the moment H&F crops ship, giving every downstream station a reason to be connected rather than standalone.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **Overshot Water Wheel power tier** — a higher-throughput wheel upgrade that speeds connected stations (shorter Cut/Hammer/Smelt timers); fits the "scale up your infrastructure" arc.
- **Water Reservoir + intake/aqueduct pieces** — offline water buffering for seasonal rivers; fits the water-distribution theme and pairs with the Outlet/Irrigation network.
- **Fiber/grain milling** — Grinding Mill flagship recipes (hemp→fiber, grain→flour); fits the grinding-automation theme but blocked on H&F crops (Phase 4).

These live in `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md` (already specced in detail).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod WaterDrivenInfrastructure` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis WaterDrivenInfrastructure` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo WaterDrivenInfrastructure` and bump minor version |
| After ACT / CMC / H&F version bump | Re-check cross-mod UID references (iron pipeline, fishing fittings, grindables) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod WaterDrivenInfrastructure         — full health check, updates .audit/
/critical-analysis WaterDrivenInfrastructure — adversarial review
/repair-items WaterDrivenInfrastructure      — auto-fix item JSON issues
/repair-blueprints WaterDrivenInfrastructure — auto-fix blueprint JSON issues
/build-mod WaterDrivenInfrastructure         — build Release DLL
/deploy-mods WaterDrivenInfrastructure       — build + deploy to game
/update-mod-version WaterDrivenInfrastructure <ver> — bump version in all 3 files
/export-to-repo WaterDrivenInfrastructure    — push to public repo
```
