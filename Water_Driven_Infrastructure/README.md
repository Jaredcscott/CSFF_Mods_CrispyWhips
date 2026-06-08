# Water Driven Infrastructure

**Version:** 1.3.3
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.63)
**Requires:** CSFFModFramework + AdvancedCopperTools

---

## Overview

Water Driven Infrastructure adds large-scale, water-powered construction to Card Survival: Fantasy Forest. Build water wheels to harness river power, then connect them to sawmills, forges, grinding mills, ore sluices, and fishponds. Gears and saw blades are cast from copper using the AdvancedCopperTools metalworking pipeline. The Mill Race Outlet lets you tap that water supply to draw unclean water at any outdoor location — no more long treks to the river.

All 17 shipped blueprints are injected into the crafting journal automatically via `BlueprintTabs.json`.

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
| 4 | Water Mill + Mill Race + 2× Large Copper Gear + 4× Small Copper Gear + Copper Saw Blade + 2× Iron Parts + 2× Iron Bearings |

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
| 4 | 20 Planks + 10 Mud Brick + 10 Plaster + 2× Iron Parts + 2× Iron Bearings |

**Key features:**
- Max temperature 1300°; cools −40°/hour when idle
- Fuel capacity 96 units: firewood (+20), charcoal (+25), embers
- **Blast** action: +480° temperature using water power (costs fuel + 1 hour)
- **Smelt Ore**: requires 1100°+; processes Greenstone and other copper ores into ingots
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
| 4 | Line: 10 Planks + 3 Bugs + 15 Heavy Stone + 30 Stone |
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

These components can be smelted back into copper nuggets if you need to reclaim the metal.

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
- Card Survival: Fantasy Forest (EA 0.63)

### Steps

1. Install BepInEx 5.x if not already installed.
2. Deploy CSFFModFramework to `BepInEx/plugins/CSFF_Mod_Framework/`.
3. Deploy AdvancedCopperTools to `BepInEx/plugins/Advanced_Copper_Tools/`.
4. Extract this mod to `BepInEx/plugins/Water_Driven_Infrastructure/`.
5. Launch the game — check `BepInEx/LogOutput.log` for `WaterDrivenInfrastructure v1.3.3 loaded.`

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
| **ActionInterceptPatch** | Intercepts sawmill Cut, forge Blast, and Workshop Hammer All; handles inventory-backed blueprint button logic (checks station inventory before consuming) |
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

## Credits

- **Author:** Jared (crispywhips)
- **Framework:** [CSFFModFramework](https://github.com/jscott3/CSFF_Mods) + BepInEx & Harmony
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games
