# Roadmap: Water Driven Infrastructure
Version at time of writing: 1.10.13
Date: 2026-08-16
Audit score: 10/10 (consolidated 2026-08-16 — release ready; see `.audit/summary.md`)

## Current State

**Theme**: Late-game, water-powered manufacturing and automation — build large-scale infrastructure
(sawmill, forge, workshop, grinding mill, ore sluice, fishpond, mill race outlets) near rivers,
powered by water wheels and mill races, fed by WDI's own copper/iron metalworking + fastener
pipeline. For players past the early survival tier who want bulk processing. **Fully standalone
since 1.8.0** — AdvancedCopperTools is a soft/optional enhancement, no longer a hard dependency.
**New this cycle (M3, 1.10.10–1.10.13)**: recruited Partners can now be assigned to autonomously
operate FIVE stations — Grinding Mill, Ore Sluice, Sawmill, Forge, and Workshop — each grafted onto
the vanilla Partner template as a mod-authored NPCDuty (`MillDutyPatch`), plus the vanilla
`PartnerDuty_Firekeeping` for the Forge/Workshop. WDI is the fleet's proof-of-concept for the
Duties/Ownership plan.

**Content**: 23 items / 23 blueprints / 10 CT2 structures + 4 CT10 improvements / 7 perks / 30
custom images (all references resolve) / 5 NPCDuty files. 0 SelfTriggeredActions, 0 spawn Triggers.

**Stability**: **10/10 — release ready.** 0 CRITICAL, 0 DESIGN GAP, 5 non-blocking WARNING (F1
uncached per-click reflection; sub-audit staleness; ROADMAP staleness — this refresh; Cast Sheets
have no WDI-solo consumer; 5 by-design empty-InventorySlots on water-feature cards), 3 MINOR polish
items. Critical Analysis verdict: **SOLID** (0 critical / 0 mechanical / 0 design / 0 broken
promises, 2026-08-16 — current with the 2026-08-16 source commit). Acquisition coverage fully closed
(112 produced / 54 consumed, 0 unreachable, 0 dead-end). Two findings carried in older sub-reports
were re-verified against current code and confirmed **RESOLVED** this pass: (a) the framework-level
`CompatibleNPCDuties[].TargetWarpData` GUID-resolution gap (flagged 2026-08-14 in
`structures-report.md`) — fixed in `CSFFModFramework/Data/WarpResolver.cs:791-806` (base-type
`GameRegistry.GetByUid` fallback naming `NPCDutyOrDutyTagRef.Target`, committed `ed9c80585`); and
(b) the perks-report `Features.json`-missing-4-of-7-perks DESIGN GAP (`Features.json` now lists all 7).

**Open work**: none blocking — no open or pending retrospectives reference this mod, and WDI has no
exposure to any open framework retro (ships no Quests.json / SealableGates / CharacterRosterInjector
usage). One outstanding **Runtime Verification** item (does not block release): **none of the 5
station "operate" duties is confirmed firing in-game.** The only prior in-session test (Ore Sluice,
pre-1.10.13) found the duty computed `notSelectable=False` yet was never selected because native
Partner duties run `BaseWeight` 900–1000 vs. this mod's then-current 20; the corrective
`StationDutyBaseWeight = 850` is itself flagged in code as an unvalidated tuning judgement, and
`MaxPerformPerDay: 0` semantics can't be resolved from JSON alone. Recommend a play session soon, or
`/failure-digest wdi-duty-selection-weight` to give the open question a persistent home.

**Framework compliance**: **Tier 1 AND Tier 2 adopted** — `ActionRouter.Register` (11 named
handlers: `MillRaceGate`, `MillGrindAll`, `SluiceAll`, `FishpondStockTracking`, `CatchOtherFishPick`,
`FishCatchStats`, `WorkshopCraftGate`, `WorkshopCraftApply`, `HammerAll`, `BlastAll`,
`IronSmeltType`), `SpawnService.Spawn` for all runtime spawning, `TickEvents.Interval` for the
fishpond population poll, plus `CardUtil`, `Api.WorldMap` (mill-race network auto-extension),
`Api.BlueprintAlternates` (ACT fastener/sheet interop), and `CompatibleDutiesWarpData` /
`CompatibleNPCDuties` (Partner duty assignment, now framework-clean). `ActionInterceptPatch` holds no
direct Harmony patch on `ActionRoutine`/`PerformStackActionRoutine`. No deprecated/dangerous
patterns: no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no `ModLoaderVerison`; all
mutation filtered to `water_sawmill_*`/`wdi*`.

---

## Phase 0: Stabilize  *(skipped — audit score 10/10, no open retrospectives)*

Nothing blocking. The mod is release-ready as of commit `cf5da4ef9` (1.10.13). The framework-level
duty-resolution gap that briefly affected the Partner Duty Assignment feature has been fixed at the
framework layer, requiring zero WDI-side change.

---

## Phase 1: Foundation

> Table-stakes hygiene. All items below are quick and non-blocking; everything else is already clean.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **Play-verify the 5 station NPCDuties** (recruit a Partner, toggle each station's Duty Assignment marker on one at a time — Forge/Workshop only after the station is already heated — confirm the companion walks over and performs Grind All / Sluice All / Cut / Smelt Ore / Hammer All without a native duty perpetually winning), OR `/failure-digest wdi-duty-selection-weight` to persist the unvalidated `BaseWeight=850` question | Runtime verification / tracking | P1 | Medium (in-game) |
| **README Version History**: move "(current)" off `v1.10.8`; add `v1.10.9`–`v1.10.13` entries (Ore Sluice / Sawmill / Forge / Workshop operate-duties + the `BaseWeight` 20→850 fix) | Docs honesty | P2 | Quick |
| **[F1]** Cache `FieldInfo` lookups in `ResolveGrindResult`/`GetHammerHitInfo` (`Patcher/ActionInterceptPatch.cs:704, 1699`) by `Type`, mirroring the `_cardModelCache`/`EnsureReflection` pattern already in this file | Perf hygiene (non-blocking) | P3 | Quick |
| Re-run `/audit-items`, `/audit-blueprints`, `/audit-structures`, `/audit-images`, `/audit-perks` for `WaterDrivenInfrastructure` | Audit hygiene | P3 | Quick |

> **Sub-audit staleness (informational).** items (2026-07-27), structures (2026-08-14), and
> blueprints/images/perks (stamped 2026-08-15) all predate the 2026-08-16 source commit and do not
> cover the M3 NPCDuty expansion (4 new `NPCDuty/*.json` + 4 modified `Location/*.json` wiring
> edits). None of their findings are contradicted by the current pass — the fresh 2026-08-16
> preflight independently confirms 0 unreachable items and clean structure wiring against current
> source — but the reports should be refreshed before being cited as covering current state.

> Already clean and requiring no action: versions synchronized (1.10.13 across ModInfo/Plugin.cs/
> README header); bin/Release sync clean; Chinese parity CLEAN (414/414 keys); all catch blocks log;
> single startup LogInfo line; `Features.json` lists all 7 perks (DESIGN GAP confirmed resolved).

---

## Phase 2: Core Expansion

> The most impactful content additions that extend the core loop. All are specced in
> `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md`.

### Fish Funnel (Near-Term, fully specced)
**What**: River-placed CT10 EnvImprovement that boosts an adjacent vanilla `FunnelTrapLocation`
fish-population rate (+100%). Chain: `Bp_FishFunnel` → `FishFunnel_Kit (CT0)` →
`FishFunnel_Placed (CT10, tag_River)`. 3 JSON + 11 CSV rows + 1 tab entry.
**Why**: Extends the fishing subsystem beyond the Fishpond; gives rivers an active WDI improvement.
**Requires**: none (re-verified this pass — no `Bp_FishFunnel.json` among the 23 shipped blueprint
files). Try the pure-JSON PassiveEffect path (Path A) first; fall back to a `WorldTickPatch.cs`
postfix only if it doesn't fire. Read vanilla `FunnelTrapLocation.json` to confirm the population
`SpecialDurability` index before authoring.
**Complexity**: Medium

### Fish Drying Rack (Near-Term)
**What**: Raw pond/funnel fish → dried fish via FuelCapacity/Wetness, completing pond → funnel →
preserve.
**Why**: Closes the classic food-without-preservation gap — the fishing loop currently yields only
raw fish.
**Requires**: Check no sibling mod already claims fish preservation first.
**Complexity**: Medium

### Cast Iron Sheet internal sink (parity)
**What**: Give `water_sawmill_cast_iron_sheet` a genuine WDI-internal consumer (higher-tier iron
station/upgrade `RequiredElement`, an iron reinforcement, or an alternate ingredient on an
iron-parts blueprint).
**Why**: Delivers the "ships its own copper AND iron cast sheets" parity `ModInfo.json` already
advertises — without ACT installed, Cast Iron Sheet currently has zero in-mod use (Cast Copper Sheet
at least doubles as the Workshop's default Hammer output). Design note, not a defect.
**Requires**: none. Use `Api.BlueprintAlternates` for any cross-mod widening.
**Complexity**: Quick–Medium

---

## Phase 3: Integration & Depth

> Cross-mod hooks and content for experienced players.

### Extend Partner Duty Assignment to remaining actions
**What**: Now that the framework's `CompatibleNPCDuties` resolution gap is fixed and 5 station
operate-duties ship, extend duty coverage to any station actions not yet wired (e.g. Fishpond catch),
and re-tune `StationDutyBaseWeight` if the play-verify step shows 850 still loses to some native duty.
**Why**: Duty assignment is the mod's newest system; the value is realized only once players actually
see companions working the stations.
**Requires**: First confirm the 5 shipped duties fire in-game (Phase 1 Runtime Verification) so the
pattern is proven before extending it further.
**Complexity**: Medium (per station)

### Water-powered Sharpening Wheel (Grinding Mill 2nd job)
**What**: A "Sharpen" interaction on the Grinding Mill restoring UsageDurability on blades.
**Why**: The Grinding Mill's manual interaction surface is still single-purpose ("Grind All" only;
the new operate-duty automates the same action, it doesn't add a second one).
**Requires**: **Design gate** — repo convention forbids repair mechanics for crafted metal items
(memory `feedback_no_repair_mechanics`). Confirm the carve-out with the user BEFORE building.
**Complexity**: Medium

### AdvancedCopperTools bronze tier
**What**: Bronze gear/bearing tier gated on ACT billets (Tin=120, bronze 130/140); ACT billets →
WDI Overshot Wheel upgrade.
**Why**: Deepens the existing (live) fastener/sheet interop into a shared power/tier model.
**Requires**: Coordination with ACT; a shared-power/load design decision (see Long-term Vision).
**Complexity**: Complex

### Community_Mod_Chest fishing-rod fittings
**What**: A WDI Workshop "Forge Iron Fittings" recipe; finished CMC rod consumes them and feeds
WDI Fishpond/Fish Funnel.
**Why**: Natural producer/consumer bridge between WDI metalworking and CMC fishing.
**Requires**: Decide the UID owner up front (memory `feedback_cross_mod_output_dependency`).
**Complexity**: Medium

### HerbsAndFungi Grinding Mill recipes
**What**: Grinding Mill flagship recipes (hemp → fiber, grain → flour); Irrigation Chain crops.
**Why**: Gives the Grinding Mill real throughput content beyond its current single grind action.
**Requires**: Blocked on H&F crops shipping.
**Complexity**: Medium

---

## Phase 4: Polish

> Art, animation, and text refinements. Art coverage is already strong (30 custom PNGs, all
> references resolve).

| Item | What | Complexity |
|------|------|------------|
| Fishpond conditional thaw | Thaw Winter → Stocked directly when frozen pop is above threshold; removes the spring population dip. `FishpondPopulationPatch` already computes the threshold. | Quick |
| `tag_SmeltsAt1100` clarity note | Copper gears' passive smelting fires at ≥900°C while the tag name implies ≥1100°C. Add a code/doc comment — do NOT rename (save-visible data). | Quick |
| GIF animation candidates | Water Wheel / Grinding Mill idle-active rotation on a powered state. | Medium |

---

## Long-term Vision

> Where this mod should be at v2.0.

WDI's natural endpoint is a **connected water-power grid**: mill races carry flow (and irrigation)
between environments, a load/tier power model (overshot vs undershot wheels) gates how many stations
a single wheel can drive, and the fishing + metalworking + milling subsystems each close their own
producer → processor → preserve loop. Autonomous Partner duties become the default way players operate
mid-tier stations once built, freeing the player for expansion — once the 5-station duty pilot is
confirmed working in-game. The v2.0 centerpiece is the **Irrigation Mill Race Chain** (directional
segments carrying flow between environments; a terminal race auto-tops TilledField/GardenPlot
Hydration) — fully specced (6 JSON + 16 CSV + BFS in `IrrigationChainPatch.cs`) but blocked on H&F
crops for meaningful content.

**Potential major additions** (not yet justified — revisit after Phase 3):
- Irrigation Mill Race Chain — the v2.0 centerpiece; makes mill races a flow network, not just a
  placement gate. Blocked on H&F crops.
- Overshot Wheel power-tier / shared-load model — turns "stations near a river" into a real capacity
  constraint; also unblocks the ACT bronze-gear tier.
- Water Reservoir — large-scale CT2 water storage as an offline buffer for outlets when river access
  is seasonal (player-requested, Sirus23 Discord 2026-08-08).
- Structure maintenance/wear loop — `UsageDurability` drain on wheels/races consuming Planks, giving
  infrastructure an upkeep cost (currently permanent once built).

These live in `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod WaterDrivenInfrastructure` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log`. **NStrip note:** WDI references `lib/Assembly-CSharp-nstrip.dll` — it must be regenerated with NStrip on the new game binary (not a simple copy). |
| After fixing a critical issue | Run `/critical-analysis WaterDrivenInfrastructure` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo WaterDrivenInfrastructure` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod WaterDrivenInfrastructure         - full health check, updates .audit/
/critical-analysis WaterDrivenInfrastructure - adversarial review
/repair-items WaterDrivenInfrastructure      - auto-fix item JSON issues
/repair-blueprints WaterDrivenInfrastructure - auto-fix blueprint JSON issues
/build-mod WaterDrivenInfrastructure         - build Release DLL
/deploy-mods WaterDrivenInfrastructure       - build + deploy to game
/update-mod-version WaterDrivenInfrastructure <ver> - bump version in all 3 files
/export-to-repo WaterDrivenInfrastructure    - push to public repo
```
