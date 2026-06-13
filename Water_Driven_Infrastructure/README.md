# Water Driven Infrastructure

**Version:** 1.6.0
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.64f)
**Requires:** CSFFModFramework + AdvancedCopperTools

---

## Overview

Water Driven Infrastructure adds large-scale, water-powered construction to Card Survival: Fantasy Forest. Build water wheels to harness river power, then connect them to sawmills, forges, grinding mills, ore sluices, and fishponds. Gears and saw blades are cast from copper using the AdvancedCopperTools metalworking pipeline. The Mill Race Outlet lets you tap that water supply to draw unclean water at any outdoor location — no more long treks to the river.

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
| **Metal Crafts** | Cast Large Copper Gear, Cast Small Copper Gear, Cast Copper Saw Blade, Forge Iron Parts, Forge Iron Bearing, Forge Iron Axle, Forge Iron Wrench |
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
| 4 | Water Mill + Mill Race + 2× Large Copper Gear + 4× Small Copper Gear + Copper Saw Blade + 2× Iron Parts + 2× Iron Bearings + 1× Iron Axle + 12× Copper Nails |

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
| 4 | 20 Planks + 10 Mud Brick + 10 Plaster + 2× Iron Parts + 2× Iron Bearings + 1× Iron Axle + 10× Copper Nails |

**Key features:**
- Max temperature 1300°; cools −40°/hour when idle
- Fuel capacity 96 units: firewood (+20), charcoal (+25), embers
- **Blast** action: +480° temperature using water power (costs fuel + 1 hour)
- **Smelt Ore** (copper): requires 1100°+; processes Greenstone and other copper ores into ingots
- **Smelt Iron Components** (Parts/Bearing/Axle/Wrench): requires 1300°; smelts WDI iron items back to a wrought iron bar
- Automatically copies vanilla kiln and smelting recipes; greenstone and copper ore smelting built in

### Water-Driven Workshop (Upgrade)

An upgrade on top of the forge adding batch metalworking and 14 inventory slots.

**Build** (unlock 32 ticks): requires existing Forge Kit + Water Mill + Bellows + charcoal + 2 Iron Parts + 2 Iron Bearings + 1 Iron Wrench + gears

**Additional actions:**
- **Hammer All** (30 min): applies water-hammer to all inventory contents simultaneously
- **Hammer Copper Sheet** (1 hour): 6 copper nuggets → 1 metal sheet
- **Forge Copper Nails** (1 hour): 6 copper nuggets → 6 copper nails

The Workshop is tagged `tag_SmeltingContainer` so vanilla PassiveEffects on ore items work correctly.

---

## Water-Driven Grinding Mill

The water wheel drives the millstone — automates all grinding tasks.

**Build** (unlock 96 ticks, requires Water Mill): Water Mill + Grinding Stone + 20 Planks + 10 Stone

---

## Ore Sluice

Uses flowing water to separate and concentrate mineral deposits.

**Two-stage build** (unlock 16 ticks, requires Mill Race):

| Stage | Materials |
|-------|-----------|
| 1 — Sluice Frame | 12 Planks + 6 Copper Nails + hammering tool |
| 2 — Ore Sluice | 10 Planks + 4 Stone + 1 Sluice Frame |

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
| 4 | Line: 10 Planks + 4 Copper Nails + 3 Bugs + 15 Heavy Stone + 30 Stone |
| 5 | Supplement: 2 Pike + 2 Perch + 2 Minnow |

Fish population grows over time and can be harvested periodically.

---

## Copper Gear Components

Copper gears, the copper saw blade, iron parts, iron bearings, and the iron wrench are required for advanced water-driven machinery. Cast copper components through the AdvancedCopperTools crucible pipeline, forge iron parts, bearings, axles, and wrenches from wrought iron bars, then use them in WDI construction blueprints.

| Blueprint | Requires | Unlock | Ingredients |
|-----------|----------|:------:|-------------|
| **Cast Large Copper Gear** | Large Crucible of Molten Copper | 16 ticks | large molten copper crucible + mold + hammer |
| **Cast Small Copper Gear** | Small Crucible of Molten Copper | 8 ticks | small molten copper crucible + mold + hammer |
| **Cast Copper Saw Blade** | Large Crucible of Molten Copper | 16 ticks | two large molten copper crucibles + saw-blade mold + hammer |
| **Forge Iron Parts** | Wrought Iron Bar | 8 ticks | wrought iron bar + hammer |
| **Forge Iron Bearing** | Wrought Iron Bar | 8 ticks | wrought iron bar + hammer |
| **Forge Iron Axle** | Wrought Iron Bar | 8 ticks | wrought iron bar + hammer |
| **Forge Iron Wrench** | Wrought Iron Bar | 8 ticks | wrought iron bar + hammer |

**Copper gears and the saw blade** smelt back to copper nuggets in the furnace or WDI Forge (1100°+).

**Iron components** (Parts, Bearing, Axle, Wrench) smelt back to a wrought iron bar in the WDI Forge or Workshop at 1300°. The forge is tagged `tag_SmeltingContainerIron` for this purpose; a standard vanilla furnace will not melt iron components.

---

## Character Creation Perks

All perks cost **Moons** and appear in the **Situational** tab.

| Perk | Cost | Starting Items |
|------|-----:|---------------|
| **Forge Start** | 8 Moons | Water-Driven Forge Kit + 1 Mill Race component |
| **Sawmill Start** | 8 Moons | Water-Driven Sawmill Frame |
| **Grinding Mill Start** | 8 Moons | Water-Driven Grinding Mill Kit + 1 Mill Race component |

---

## Installation

### Requirements

- BepInEx 5.x
- CSFFModFramework (latest)
- **AdvancedCopperTools** (hard dependency — provides the copper ingot pipeline)
- Card Survival: Fantasy Forest (EA 0.64f)

### Steps

1. Install BepInEx 5.x if not already installed.
2. Deploy CSFFModFramework to `BepInEx/plugins/CSFF_Mod_Framework/`.
3. Deploy AdvancedCopperTools to `BepInEx/plugins/Advanced_Copper_Tools/`.
4. Extract this mod to `BepInEx/plugins/Water_Driven_Infrastructure/`.
5. Launch the game — check `BepInEx/LogOutput.log` for `WaterDrivenInfrastructure v1.6.0 loaded.`

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
├── Localization/SimpEn.csv
└── Resource/Picture/
```

---

## Harmony Patches

| Patch | Purpose |
|-------|---------|
| **GameLoadPatch** | Loads mod data at startup |
| **MillRaceNetwork** | Bidirectional mill race connectivity — both endpoints of a race must be complete before the connection activates; a single directional segment cannot power structures |
| **ActionInterceptPatch** | Intercepts sawmill Cut, forge Blast, and Workshop Hammer All; handles inventory-backed blueprint button logic (checks station inventory before consuming); sets SD4=200 (iron metal type) on wrought iron bars spawned when iron components finish smelting |
| **FishpondPopulationPatch** | Fishpond population growth and periodic harvesting mechanics |

All patches filter on this mod's UniqueIDs and never modify vanilla cards, drops, or stats.

---

## Compatibility

- **AdvancedCopperTools** is required — this mod builds on its copper ingot and metalworking pipeline.
- Works alongside HerbsAndFungi, RepeatAction, SkillSpeedBoost, SheepHusbandry, and other framework mods.
- Safe to add to an existing save. Removing mid-save causes modded structures to disappear but does not corrupt the save file.

---

## Troubleshooting

**Blueprints not appearing?** Verify both CSFFModFramework and AdvancedCopperTools are installed. Check `LogOutput.log` for their load messages.

**Forge won't smelt?** Temperature must reach 1100°. Feed charcoal and use the Blast action before attempting to smelt.

**Building from source?** The project intentionally references `lib/Assembly-CSharp-nstrip.dll` as its compile-time game assembly. A separate `lib/Assembly-CSharp.dll` is not required for this mod.

**Mill Race Outlet not producing water?** The outlet must be outdoors and a Mill Race must be built first. Outlets freeze in winter — wait until spring.

**Workshop Hammer All does nothing?** Load tool blanks or copper components into the workshop's inventory slots first.

**Sawmill Cut action missing?** The sawmill must be placed (not held as a kit) and a log must be dragged onto it.

---

## Version History

### v1.6.0 (current)
- Mill race network now automatically incorporates world-map locations added by other mods via CSFFModFramework 2.7.0+ `Api.WorldMap` (e.g. CMC's Village Path) — no manual edge-file edits needed.
- Tested with EA 0.64f.

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
