# Water Driven Infrastructure

**Version:** 1.8.2
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.65)
**Requires:** CSFFModFramework (AdvancedCopperTools optional — enhances, doesn't gate)

---

## Overview

Water Driven Infrastructure adds large-scale, water-powered construction to Card Survival: Fantasy Forest. Build water wheels to harness river power, then connect them to sawmills, forges, grinding mills, ore sluices, and fishponds. Gears and saw blades are cast from copper using WDI's own metalworking pipeline. The Mill Race Outlet lets you tap that water supply to draw unclean water at any outdoor location — no more long treks to the river.

Fully standalone: WDI ships its own copper fasteners (Cast Copper Rivet, Alloy Solder, Cast Metal Sheet) so every blueprint builds with just the framework installed. If **AdvancedCopperTools** is also installed, its Copper Nails, Tin Solder, and Copper Sheet are accepted interchangeably in every fastener slot, and the Workshop's Hammer Copper Sheet / Forge Copper Nails actions produce ACT's real items instead of WDI's.

All 18 shipped blueprints are injected into the crafting journal automatically via `BlueprintTabs.json`.

---

## Infrastructure Chain

```
Water Source (river / lake)
 └── Mill Race ──────────────────────→ Mill Race Outlet  (draw water anywhere outdoors)
                   └── Water Wheel
                          └── Water Mill
                                 ├── Water-Driven Sawmill
                                 ├── Water-Driven Forge ──→ Water-Driven Workshop (upgrade)
                                 ├── Water-Driven Grinding Mill
                                 ├── Ore Sluice
                                 └── Fishpond
```

---

## Content

### Blueprint Tabs

| Tab | Blueprints |
|-----|-----------|
| **Advanced Tools** | Mill Race, Mill Race Outlet, Water Wheel, Water Mill, Ore Sluice (Empty) |
| **Metal Crafts** | Cast Large Copper Gear, Cast Small Copper Gear, Cast Copper Saw Blade, Forge Iron Parts, Forge Iron Bearing, Forge Iron Axle, Forge Iron Wrench, Forge Copper Rivet, Forge Alloy Solder, Forge Cast Metal Sheet |
| **Furniture** | Ore Sluice, Water-Driven Grinding Mill, Water-Driven Sawmill, Water-Driven Forge, Water-Driven Workshop Kit |
| **Farming Agriculture** | Fishpond |

Mill Race directional improvements (N/S/E/W) appear in the **Environment Improvements** panel of each outdoor location — injected per-location at runtime; they do not appear in the crafting journal.

The mill-race network is built from a static world-map edge list (`Data/MillRaceMapEdges.json`). Since 1.6.0, locations added to the world map by other mods through CSFFModFramework 2.7.0+ (`WorldMap/MapNodes.json`) are appended to the network automatically via `Api.WorldMap` — no edits to the static map file needed.

---

## Mill Race & Water Outlets

**Mill Race** — a wooden channel that directs river or lake water to your machines.

- Must be placed adjacent to a water source (river, lake, or stream)
- Unlocked: 16 ticks research; 1-stage build
- Required by most downstream blueprints

**Mill Race Outlet** — taps the Mill Race to provide freely drawable unclean water anywhere outdoors.

- Build one Mill Race, then construct the outlet at any outdoor location
- Provides effectively unlimited unclean water (purify before drinking)
- Freezes in winter; thaws in spring
- Unlock: 32 ticks research

---

## Water Wheel & Water Mill

**Water Wheel** — the primary power source.

| Field | Value |
|-------|-------|
| Requires | 25 Planks, 20 Stone, 5 Clay, 1 Large Copper Gear |
| Placement | Adjacent to a Mill Race |
| Unlock | 48 ticks |

**Water Mill** — converts wheel rotation into usable mechanical power; the base for all downstream machines.

| Field | Value |
|-------|-------|
| Requires | 1 Water Wheel + 1 Mill Race + 1 Large Copper Gear |
| Unlock | 48 ticks |

---

## Water-Driven Sawmill

Automated wood processing — drag logs in, collect planks.

**Multi-stage build** (unlock 96 ticks, requires Water Mill):

| Stage | Materials |
|-------|-----------|
| 1 | 25 Heavy Stone + 10 Plaster + Wooden Shovel (keep) + Metal Shovel (keep) |
| 2 | 8 Rope |
| 3 | Forge Hammer (keep) + 20 Planks |
| 4 | Water Mill + Mill Race + 2× Large Copper Gear + 4× Small Copper Gear + Copper Saw Blade + 2× Iron Parts + 2× Iron Bearings + 1× Iron Axle + 12× Copper Rivets* |

**Key features:**
- **Cut** action (30 min): drag a log onto the sawmill → 8 Planks
- 6-slot inventory; holds logs awaiting processing
- **Pack Up** dismantle (1 hour): recovers all components as a portable kit

---

## Water-Driven Forge

Water-powered forge that smelts at 1100°+ and adds a water-hammer for batch metalworking.

**Multi-stage build** (unlock 64 ticks, requires Water Mill):

| Stage | Materials |
|-------|-----------|
| 1 | Water Mill + Wooden Shovel (keep) + Metal Shovel (keep) |
| 2 | 40 Stone + 40 Mud Brick + 10 Planks |
| 3 | 10 Clay + 1 Leather Bellows + 10 Planks |
| 4 | 20 Planks + 10 Mud Brick + 10 Plaster + 2× Iron Parts + 2× Iron Bearings + 1× Iron Axle + 10× Copper Rivets* |

**Key features:**
- Max temperature 1800°; cools −40°/hour when idle
- Fuel capacity 96 units: firewood (+20), charcoal (+25), embers
- **Blast** action: +480° temperature using water power (costs fuel + 1 hour)
- **Smelt Ore** (copper): requires 1100°+; processes Greenstone and other copper ores into ingots
- **Smelt Iron Components** (Parts/Bearing/Axle/Wrench): requires 1100°+ (same threshold as copper); each item automatically smelts back into 6 iron-typed metal nuggets once fully heated — not into a bar
- Automatically copies vanilla kiln and smelting recipes; greenstone and copper ore smelting built in

### Water-Driven Workshop (Upgrade)

An upgrade on top of the forge adding batch metalworking and 14 inventory slots.

**Build** (unlock 32 ticks): requires existing Forge Kit + Water Mill + Leather Bellows + 10 Charcoal + 2 Iron Parts + 2 Iron Bearings + 1 Iron Axle + 1 Iron Wrench + 2 Small Copper Gears + 1 Large Copper Gear + 8 Copper Rivets* + 1 Alloy Solder*

**Additional actions:**
- **Hammer All** (30 min): applies water-hammer to all inventory contents simultaneously
- **Hammer Copper Sheet** (1 hour): 6 copper nuggets → 1 metal sheet
- **Forge Copper Nails** (1 hour): 6 copper nuggets → 6 copper nails

The Workshop is tagged `tag_SmeltingContainer` so vanilla PassiveEffects on ore items work correctly.

---

## Water-Driven Grinding Mill

The water wheel drives the millstone — automates all grinding tasks.

**Build** (unlock 96 ticks, requires Water Mill): Water Mill + Grinding Stone + 20 Planks + 10 Stone + 1 Iron Bearing + 1 Iron Axle + 8 Copper Rivets* + 1 Alloy Solder*

---

## Ore Sluice

Uses flowing water to separate and concentrate mineral deposits.

**Two-stage build** (unlock 16 ticks, requires Mill Race):

| Stage | Materials |
|-------|-----------|
| 1 — Sluice Frame | 12 Planks + 6 Copper Rivets* + hammering tool |
| 2 — Ore Sluice | 10 Planks + 4 Stone + 1 Sluice Frame + 6 Copper Rivets* |

Placement must be adjacent to a Mill Race.

---

## Fishpond

A dug and stocked pond for sustained fish production.

**Multi-stage build** (unlock 64 ticks, requires Mill Race):

| Stage | Action |
|-------|--------|
| 1 | Dig with shovel (−25 durability) |
| 2 | Dig with shovel (−25 durability) |
| 3 | Dig with shovel (−25 durability) |
| 4 | Line: 10 Planks + 4 Copper Rivets* + 3 Bugs + 15 Heavy Stone + 30 Stone |
| 5 | Supplement: 2 Pike + 2 Perch + 2 Minnow |

Fish population grows over time and can be harvested periodically. The pond freezes in winter and cannot be packed up until it thaws in spring.

---

## Copper Gear Components & Fasteners

Copper gears, the copper saw blade, iron parts, iron bearings, and the iron wrench are required for advanced water-driven machinery. Cast copper components through WDI's own crucible pipeline, forge iron parts, bearings, axles, and wrenches from wrought iron bars, then use them in WDI construction blueprints.

| Blueprint | Requires | Unlock | Ingredients |
|-----------|----------|:------:|-------------|
| **Cast Large Copper Gear** | Large Crucible of Molten Copper | 16 ticks | large molten copper crucible + mold + hammer |
| **Cast Small Copper Gear** | Small Crucible of Molten Copper | 8 ticks | small molten copper crucible + mold + hammer |
| **Cast Copper Saw Blade** | Large Crucible of Molten Copper | 16 ticks | one large molten copper crucible + saw-blade mold + hammer |
| **Forge Iron Parts** | Wrought Iron Bar (iron-typed) | 8 ticks | 1 wrought iron bar → 2 Iron Parts (no tool needed) |
| **Forge Iron Bearing** | Wrought Iron Bar (iron-typed) | 8 ticks | 1 wrought iron bar → 1 Iron Bearing (no tool needed) |
| **Forge Iron Axle** | Wrought Iron Bar (iron-typed) | 8 ticks | 1 wrought iron bar → 1 Iron Axle (no tool needed) |
| **Forge Iron Wrench** | Wrought Iron Bar (iron-typed) | 8 ticks | 1 wrought iron bar → 1 Iron Wrench (no tool needed) |
| **Forge Copper Rivet** | Copper-grade Metal Nugget | 8 ticks | 1 nugget + hammer (kept) → 1 Copper Rivet |
| **Forge Alloy Solder** | Copper-grade Metal Nugget | 8 ticks | 1 nugget + hammer (kept) → 2 Alloy Solder |
| **Forge Cast Metal Sheet** | Heated Copper-grade Metal Bar | 8 ticks | 1 heated bar + hammer (kept) → 1 Cast Metal Sheet |

**Copper gears and the saw blade** smelt back to copper nuggets in the furnace or WDI Forge (1100°+).

**Iron components** (Parts, Bearing, Axle, Wrench) smelt back into iron-typed metal nuggets (6 per item) in the WDI Forge or Workshop once heated to 1100°+ — the same threshold as copper, not 1300°. The forge is tagged `tag_SmeltingContainerIron` for this purpose; a standard vanilla furnace will not melt iron components.

**\* Fasteners (Copper Rivets / Alloy Solder) — interchangeable with AdvancedCopperTools:** every blueprint slot that calls for a Copper Rivet or Alloy Solder also accepts ACT's Copper Nail / Tin Solder if AdvancedCopperTools is installed — craft whichever you already have. WDI's own Copper Rivet and Alloy Solder blueprints are always researchable, independent of whether ACT is present, and forge from a plain copper-grade Metal Nugget (no tin ore or ACT-exclusive materials required).

---

## Character Creation Perks

All perks appear in the **Situational** tab.

| Perk | Cost | Starting Items |
|------|-----:|---------------|
| **Forge Start** | 1 Star | Water-Driven Forge Kit + 1 Mill Race component |
| **Sawmill Start** | 2 Moons | Water-Driven Sawmill Frame |
| **Grinding Mill Start** | 2 Moons | Water-Driven Grinding Mill Kit + 1 Mill Race component |

---

## Installation

### Requirements

- BepInEx 5.x
- CSFFModFramework (latest)
- Card Survival: Fantasy Forest (EA 0.65)
- **AdvancedCopperTools** (optional — enhances fastener/Workshop output, not required to build or research anything)

### Steps

1. Install BepInEx 5.x if not already installed.
2. Deploy CSFFModFramework to `BepInEx/plugins/CSFF_Mod_Framework/`.
3. (Optional) Deploy AdvancedCopperTools to `BepInEx/plugins/Advanced_Copper_Tools/`.
4. Extract this mod to `BepInEx/plugins/Water_Driven_Infrastructure/`.
5. Launch the game — check `BepInEx/LogOutput.log` for `WaterDrivenInfrastructure v1.8.2 loaded.`

### Deployed layout

```
BepInEx/plugins/Water_Driven_Infrastructure/
├── Water_Driven_Infrastructure.dll
├── ModInfo.json
├── BlueprintTabs.json
├── SmeltingRecipes.json
├── CardData/
│   ├── Blueprint/
│   ├── EnvImprovement/
│   ├── Item/
│   └── Location/
├── CharacterPerk/
├── Localization/
│   ├── SimpEn.csv
│   └── SimpCn.csv
└── Resource/Picture/
```

---

## Harmony Patches

| Patch | Purpose |
|-------|---------|
| **GameLoadPatch** | Loads mod data at startup; injects Mill Race directional improvements into world-map locations, copies vanilla kiln/smelting recipes into the forge and workshop, and fixes the runtime iron-smelting container tag |
| **MillRaceNetwork** | Bidirectional mill race connectivity — both endpoints of a race must be complete before the connection activates (a single directional segment cannot power structures); also gates outlet/station placement, water draws, and station use to locations with direct water or a connected, unfrozen outlet, with a winter-freeze bypass when a lit Copper Brazier is nearby |
| **ActionInterceptPatch** | Registers ActionRouter handlers for Grind All (mill), Sluice All (ore sluice), fishpond stocking/catch stats, Workshop crafts (Hammer Copper Sheet / Forge Copper Nails / Cast Metal Lump), Hammer All, and forge/workshop Blast; checks station inventory before consuming for inventory-backed buttons; sets SD4=200 (iron metal type) and quality on the iron-typed metal nuggets spawned when iron components finish smelting. (Sawmill Cut is pure JSON and is not intercepted here.) |
| **FishpondPopulationPatch** | Fishpond population growth: gates breeding per species (needs ≥2 individuals) and swaps the pond between Filled ↔ Stocked card variants once total population crosses the stocking threshold (10) |

All patches filter on this mod's UniqueIDs and never modify vanilla cards, drops, or stats.

---

## Compatibility

- **AdvancedCopperTools is optional**, declared as `BepInDependency.DependencyFlags.SoftDependency` — WDI is fully playable with only CSFFModFramework installed. When ACT is also present, its Copper Nail, Tin Solder, and Copper Sheet are accepted interchangeably in every WDI fastener slot, and the Workshop's Hammer Copper Sheet / Forge Copper Nails actions produce ACT's real items instead of WDI's own.
- Works alongside HerbsAndFungi, RepeatAction, SkillSpeedBoost, SheepHusbandry, and other framework mods.
- Safe to add to an existing save. Removing mid-save causes modded structures to disappear but does not corrupt the save file.

**Dependency chain:** `CSFFModFramework` → `WaterDrivenInfrastructure` (soft, enhanced by: `AdvancedCopperTools`). No other mod depends on WDI.

---

## Troubleshooting

**Blueprints not appearing?** Verify CSFFModFramework is installed and check `LogOutput.log` for `WaterDrivenInfrastructure v1.8.2 loaded.` AdvancedCopperTools is optional.

**Forge won't smelt?** Temperature must reach 1100°. Feed charcoal and use the Blast action before attempting to smelt.

**Building from source?** The project intentionally references `lib/Assembly-CSharp-nstrip.dll` as its compile-time game assembly. A separate `lib/Assembly-CSharp.dll` is not required for this mod.

**Mill Race Outlet not producing water?** The outlet must be outdoors and a Mill Race must be built first. Outlets freeze in winter — wait until spring.

**Workshop Hammer All does nothing?** Load tool blanks or copper components into the workshop's inventory slots first.

**Sawmill Cut action missing?** The sawmill must be placed (not held as a kit) and a log must be dragged onto it.

---

## Version History

### v1.8.0 (current)
- **Removed the hard dependency on AdvancedCopperTools.** WDI now loads and is fully playable with only CSFFModFramework installed; ACT is a soft/optional enhancement.
- Added 3 WDI-native items + blueprints: Cast Copper Rivet, Alloy Solder, Cast Metal Sheet (Metal Crafts tab) — forged from a plain copper-grade Metal Nugget, always researchable.
- The 12 blueprint slots that previously required ACT's Copper Nail / Tin Solder now require WDI's own Copper Rivet / Alloy Solder instead, and additionally accept ACT's originals interchangeably when ACT is installed.
- Workshop's Hammer Copper Sheet / Forge Copper Nails actions now produce ACT's real items when ACT is installed, and WDI's own Cast Metal Sheet / Copper Rivet otherwise.
- Framework gained a new shared helper, `CSFFModFramework.Api.BlueprintAlternates`, generalizing the alternate-ingredient pattern AdvancedCopperTools already used for iron/copper nail interchangeability.

### v1.7.3
- GameLoadPatch: replaced a silent no-op guard with explicit error logging at every reflection step (`GameLoad` type, instance, `DataBase`, `AllData`) and a single Info summary line on success. Previously, if any upstream reflection call resolved to null, the entire postfix (kiln recipe copy, greenstone smelt fix, iron container tag, mill race improvements) skipped with zero log output and no exception — indistinguishable from a working load in the default BepInEx log filter.

### v1.7.2
- Fishpond: build time reduced from 12 to 8 DTP/stage (5 stages: 15 hours → 10 hours total), bringing it in line with the mod's other multi-stage structures (Sawmill/Forge total 12 hours across 4 stages). Description and localization updated to match.

### v1.7.1
- Forge: added 14 inventory slots so items can be loaded into the forge for smelting.
- Iron components (Parts, Bearing, Axle, Wrench): added `tag_SmeltsAt1100` so these items are accepted by the forge and workshop smelting station inventory filters and smelt at the same threshold as copper.
- Copper Saw Blade: removed duplicate OnFull auto-smelt that was conflicting with the progress-based smelting system.
- Workshop Kit: added pack-up time (`DeconstructDaytimeCost: 3`).
- Fishpond Winter: description now clarifies the pond cannot be packed up while frozen.
- Blueprint / location stage ProgressRange fixes for Water Wheel and Grinding Mill.

### v1.6.0
- Mill race network now automatically incorporates world-map locations added by other mods via CSFFModFramework 2.7.0+ `Api.WorldMap` (e.g. CMC's Village Path) — no manual edge-file edits needed.
- Tested with EA 0.65.

### v1.5.0
- Quality invariant: machine outputs never spawn below input quality; Blast All nuggets inherit best input quality (floor 50%).
- Natural Windflow: forge and workshop gain the vanilla wind passive (+8 windflow, +120° in windy environments), matching vanilla Forge/Bloomery/Furnace.
- Hammer dedup: single wrap point prevents one press from routing through both PerformStackActionRoutine and ActionRoutine (was doubling strikes or double-charging fish-catch tick).
- UnfinishedLump eager stat init so Hammer All works immediately after Cast Metal Lump.

### v1.4.0
- Simplified Chinese localization for all cards, blueprints, and perks.

### v1.3.4
- Fixed iron item smelting: Iron Parts, Bearing, Axle, and Wrench now correctly produce a wrought iron bar (SD4=200, iron metal type) when smelted in the WDI Forge at 1300°. Previously the spawned bar defaulted to SD4=0 (copper type), causing it to fail the iron-ingredient gate on downstream blueprints.

### v1.3.3
- EA 0.63f compatibility; blueprint tab injector updated to live UI tabs (fixes journal tab disappearing on EA 0.63f)

### v1.3.2
- Workshop storage pattern: replaced `ContainedBlueprintCardsWarpData` with DismantleAction buttons + Harmony handlers; eliminates inventory display conflict that occurred with station-contained operation blueprints
- `BlueprintContainerSaveLoadFix`: separated save-load and gameplay paths to prevent freeze when ModCore is installed

### v1.3.0
- Water-Driven Workshop (Forge upgrade): Hammer All, Hammer Copper Sheet, Forge Copper Nails — 14 inventory slots
- Iron component blueprints: Forge Iron Parts, Bearing, Axle, Wrench from wrought iron bars

### v1.2.x
- Water-Driven Forge: Blast action, 1300° max temperature, tagged `tag_SmeltingContainer` + `tag_SmeltingContainerIron`
- Water-Driven Grinding Mill

### v1.1.x
- Mill Race Outlet: draw unclean water at any outdoor location; freezes in winter
- Ore Sluice two-stage build

### v1.0.x
- Initial release: Mill Race, Water Wheel, Water Mill, Water-Driven Sawmill, Fishpond
- Copper gear components (Large/Small Gear, Saw Blade) cast from ACT crucible pipeline

---

## Credits

- **Author:** Jared (crispywhips)
- **Framework:** [CSFFModFramework](https://github.com/jscott3/CSFF_Mods) + BepInEx & Harmony
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games
