# Water Driven Infrastructure

**Version:** 1.10.15
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.66)
**Requires:** CSFFModFramework (AdvancedCopperTools optional — enhances, doesn't gate)

---

## Overview

Water Driven Infrastructure adds large-scale, water-powered construction to Card Survival: Fantasy Forest. Build water wheels to harness river power, then connect them to sawmills, forges, grinding mills, ore sluices, and fishponds. Gears and saw blades are cast from copper using WDI's own metalworking pipeline. The Mill Race Outlet lets you tap that water supply to draw unclean water at any outdoor location — no more long treks to the river.

Fully standalone: WDI ships its own copper AND iron fasteners (Cast Copper Rivet, Cast Iron Rivet, Alloy Solder, Cast Copper Sheet, Cast Iron Sheet) so every blueprint builds with just the framework installed. Both cast sheets earn their keep without any other mod — the Cast Iron Sheet shears into eight Iron Rivets in one cold-work craft, and the Cast Copper Sheet is what the Workshop's Hammer Copper Sheet action produces when AdvancedCopperTools is absent. If **AdvancedCopperTools** is also installed, its Copper/Iron Nails, Tin Solder, and Copper/Iron Sheet are accepted interchangeably in every fastener slot (same tier for sheets; any tier for nails/rivets, since they're a generic fastener), and the Workshop's Hammer Copper Sheet / Forge Copper Nails actions produce ACT's real items instead of WDI's.

All 24 shipped blueprints are injected into the crafting journal automatically via `BlueprintTabs.json`.

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
| **Metal Crafts** | Cast Large Copper Gear, Cast Small Copper Gear, Cast Copper Saw Blade, Forge Iron Parts, Forge Iron Bearing, Forge Iron Axle, Forge Iron Wrench, Forge Copper Rivet, Forge Iron Rivet, Forge Alloy Solder, Forge Cast Copper Sheet, Forge Cast Iron Sheet, Cut Iron Rivets from Sheet |
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
| Requires | 25 Planks, 20 Stone, 5 Heavy Stone, 1 Large Copper Gear |
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

A recruited Partner can be assigned an **Operate the Sawmill** duty — toggle it on and they'll walk to the sawmill and run Cut, supplying a log themselves. *(Shipped in 1.10.11, not yet confirmed in-game.)*

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
- Max temperature 1800°; loses heat at −33°/hour whenever out of fuel, and above ~1000° loses an additional −100°/hour without windflow (a windy environment or the Blast/Bellows actions) — a third of the original cooldown rate, so the forge holds its heat much longer between Blasts
- Fuel capacity 96 units: firewood (+20), charcoal (+25), embers
- **Blast** action: +480° temperature using water power (costs fuel + 1 hour); also pushes ambient temperature toward its 80° maximum, warming the player — most noticeable in winter
- **Smelt Ore** (copper): requires 1100°+; processes Greenstone and other copper ores into ingots
- **Smelt Iron Components** (Parts/Bearing/Axle/Wrench): requires 1100°+ (same threshold as copper); each item automatically smelts back into 6 iron-typed metal nuggets once fully heated — not into a bar
- Automatically copies vanilla kiln and smelting recipes; greenstone and copper ore smelting built in
- A recruited Partner can be assigned the vanilla **fire-tending duty** to this station — they'll feed firewood, fuel, charcoal, or embers into it automatically to keep it lit

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

A recruited Partner can be assigned an **Operate the Grinding Mill** duty — toggle it on and they'll walk to the mill and press Grind All automatically.

---

## Ore Sluice

Uses flowing water to separate and concentrate mineral deposits.

**Two-stage build** (unlock 16 ticks, requires Mill Race):

| Stage | Materials |
|-------|-----------|
| 1 — Sluice Frame | 12 Planks + 6 Copper Rivets* + hammering tool |
| 2 — Ore Sluice | 10 Planks + 4 Stone + 1 Sluice Frame + 6 Copper Rivets* |

Placement must be adjacent to a Mill Race.

A recruited Partner can be assigned an **Operate the Ore Sluice** duty — toggle it on and they'll walk to the sluice and press Sluice All. *(Shipped in 1.10.10, not yet confirmed in-game.)*

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
| **Forge Iron Rivet** | Iron-grade Metal Nugget | 8 ticks | 1 nugget + hammer (kept) → 1 Iron Rivet |
| **Forge Alloy Solder** | Copper-grade Metal Nugget | 8 ticks | 1 nugget + hammer (kept) → 2 Alloy Solder |
| **Forge Cast Copper Sheet** | Heated Copper-grade Metal Bar | 8 ticks | 1 heated bar + hammer (kept) → 1 Cast Copper Sheet |
| **Forge Cast Iron Sheet** | Heated Iron-grade Metal Bar | 8 ticks | 1 heated bar + hammer (kept) → 1 Cast Iron Sheet |
| **Cut Iron Rivets from Sheet** | Cast Iron Sheet | 8 ticks | 1 Cast Iron Sheet + hammer (kept) → 8 Iron Rivets (cold work — no forge heat) |

**Copper gears and the saw blade** smelt back to copper nuggets in the furnace or WDI Forge (1100°+).

**Iron components** (Parts, Bearing, Axle, Wrench) smelt back into iron-typed metal nuggets (6 per item) in the WDI Forge or Workshop once heated to 1100°+ — the same threshold as copper, not 1300°. The forge is tagged `tag_SmeltingContainerIron` for this purpose; a standard vanilla furnace will not melt iron components.

**\* Fasteners (Copper/Iron Rivets, Alloy Solder) — interchangeable with AdvancedCopperTools:** every blueprint slot that calls for a Rivet or Alloy Solder also accepts ACT's Copper/Iron Nail or Tin Solder if AdvancedCopperTools is installed — craft whichever you already have, in whichever tier you have. Rivets are a generic fastener commodity, so copper and iron rivets/nails are all interchangeable with each other regardless of tier or mod. Cast Copper/Iron Sheet stay tier-locked with ACT's Copper/Iron Sheet (same-tier only — an iron-tier build still needs iron-tier sheet). **The Cast Iron Sheet does not depend on ACT to be worth making:** the Cut Iron Rivets from Sheet blueprint shears one sheet into 8 Iron Rivets with any hammering tool, no forge heat required. Since a sheet comes from one iron bar (6 nuggets), plate-cutting yields 8 fasteners where nugget-by-nugget forging yields 6 — the extra bar-and-heat work buys a better fastener rate, in a single craft action instead of six. WDI's own fastener blueprints are always researchable, independent of whether ACT is present, and forge from a plain copper- or iron-grade Metal Nugget/Bar (no tin ore or ACT-exclusive materials required).

---

## Character Creation Perks

All perks appear in the **Situational** tab.

| Perk | Cost | Starting Items |
|------|-----:|---------------|
| **Forge Start** | 1 Star | Water-Driven Forge Kit + 1 Mill Race component |
| **Sawmill Start** | 2 Moons | Water-Driven Sawmill Frame |
| **Grinding Mill Start** | 2 Moons | Water-Driven Grinding Mill Kit + 1 Mill Race component |
| **Prospector's Start** | 2 Moons | Ore Sluice Kit |
| **Angler's Start** | 2 Moons | Fishpond Kit |
| **Homesteader's Waterworks** | 2 Moons | Mill Race Outlet Kit + 1 Mill Race component |
| **Millwright** | 30 Suns | +75 Woodworking head start (no items) |

---

## Installation

### Requirements

- BepInEx 5.x
- CSFFModFramework (latest)
- Card Survival: Fantasy Forest (EA 0.66)
- **AdvancedCopperTools** (optional — enhances fastener/Workshop output, not required to build or research anything)

### Steps

1. Install BepInEx 5.x if not already installed.
2. Deploy CSFFModFramework to `BepInEx/plugins/CSFF_Mod_Framework/`.
3. (Optional) Deploy AdvancedCopperTools to `BepInEx/plugins/Advanced_Copper_Tools/`.
4. Extract this mod to `BepInEx/plugins/Water_Driven_Infrastructure/`.
5. Launch the game — check `BepInEx/LogOutput.log` for `WaterDrivenInfrastructure v1.10.15 loaded.`

### Deployed layout

```
BepInEx/plugins/Water_Driven_Infrastructure/
├── Water_Driven_Infrastructure.dll
├── ModInfo.json
├── BlueprintTabs.json
├── CardData/
│   ├── Blueprint/
│   ├── EnvImprovement/
│   ├── Item/
│   └── Location/
├── CharacterPerk/
├── NPCDuty/
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
| **MillDutyPatch** | Grafts five custom NPCDuties — "Operate the Grinding Mill", "Operate the Ore Sluice", "Operate the Sawmill", "Operate the Forge" and "Operate the Workshop" — onto the vanilla Partner NPCAgent template (JSON-shell + reflection-built `ActionSequence`), letting a recruited companion work each station once the player's Duty Assignment toggle is on. The mill and sluice drive a DismantleAction button; the sawmill drives the drag-based **Cut** CardInteraction, with the engine sourcing the log from that action's own `CompatibleCards`; the forge drives **Smelt Ore** and the workshop chains **Hammer All** then **Smelt Ore**. The forge/workshop duties only work a station the player has *already heated* — they never light or blast it. **None of these five is confirmed working in-game**: a play session on 2026-08-16 showed the duties losing duty selection outright to vanilla's much-higher-weighted native duties, which is addressed but unverified as of 1.10.13 |

All patches filter on this mod's UniqueIDs and never modify vanilla cards, drops, or stats.

---

## Compatibility

- **AdvancedCopperTools is optional**, declared as `BepInDependency.DependencyFlags.SoftDependency` — WDI is fully playable with only CSFFModFramework installed. When ACT is also present, its Copper Nail, Tin Solder, and Copper Sheet are accepted interchangeably in every WDI fastener slot, and the Workshop's Hammer Copper Sheet / Forge Copper Nails actions produce ACT's real items instead of WDI's own.
- Works alongside HerbsAndFungi, RepeatAction, SkillSpeedBoost, SheepHusbandry, and other framework mods.
- Safe to add to an existing save. Removing mid-save causes modded structures to disappear but does not corrupt the save file.

**Dependency chain:** `CSFFModFramework` → `WaterDrivenInfrastructure` (soft, enhanced by: `AdvancedCopperTools`). No other mod depends on WDI.

---

## Troubleshooting

**Blueprints not appearing?** Verify CSFFModFramework is installed and check `LogOutput.log` for `WaterDrivenInfrastructure v1.10.15 loaded.` AdvancedCopperTools is optional.

**Forge won't smelt?** Temperature must reach 1100°. Feed charcoal and use the Blast action before attempting to smelt.

**Building from source?** The project intentionally references `lib/Assembly-CSharp-nstrip.dll` as its compile-time game assembly. A separate `lib/Assembly-CSharp.dll` is not required for this mod.

**Mill Race Outlet not producing water?** The outlet must be outdoors and a Mill Race must be built first. Outlets freeze in winter — wait until spring.

**Workshop Hammer All does nothing?** Load tool blanks or copper components into the workshop's inventory slots first. Note that Metal Quality gains from each strike don't show as a visible number on the item itself (vanilla hides that stat's display on nuggets/blanks/bars) — the boost is still applied and carries through once the item is finished or transformed.

**Sawmill Cut action missing?** The sawmill must be placed (not held as a kit) and a log must be dragged onto it.

---

## Version History

### v1.10.15 (current)
- Fixed Natural Windflow still going dead below 790° in windy environments (e.g. High Grove) — the v1.10.5 fix moved the dead zone rather than removing it. Lowered the floor to 1°, matching "Lose Temperature without fuel"'s own floor.
- Fixed Cast Metal Lump's output (vanilla MetalBarUnfinished) never being accepted back into the Workshop's own inventory, dead-ending the Cast Metal Lump → Hammer All progression despite the Workshop's own help text claiming to accept it.
- Clarified in the Workshop's help text that Hammer All's Metal Quality boost is real (traced end-to-end and confirmed correct) but never shows as a visible number — vanilla flags that stat `AlwaysHide` on every candidate item, so the game's own UI never renders it regardless of value.

### v1.10.8
- Water-Driven Grinding Mill can now be assigned to a Partner's Duty Assignment list — toggle the mill's duty marker on and a recruited companion will walk to it and press Grind All automatically. Ships a custom NPCDuty (`NPCDuty/wdiOperateGrindingMill_Duty.json`) grafted onto the vanilla Partner template via reflection (`MillDutyPatch`).

### v1.10.7
- Water-Driven Forge and Workshop can now be assigned to a Partner's vanilla fire-tending duty (`PartnerDuty_Firekeeping`) — a recruited companion will feed firewood, fuel, charcoal, or embers into the station automatically to keep it lit.

### v1.10.6
- Blast (Forge and Workshop) now also pushes ambient temperature toward its 80° cap, via a direct `StatModifications` bump to the vanilla `BaseTemperature` GameStat (clamped by the stat's own -80..80 range, so it can never overshoot). Warms the player standing at the station — most noticeable countering the deep cold of winter.
- Forge and Workshop now hold their heat 3x longer: both cooldown `PassiveEffects` ("Lose Temperature without fuel", "High Temperature loss without windflow") cut to a third of their prior rate, so Blast is needed less often to stay at smelting temperature.

### v1.10.5
- Fixed Hammer All never raising Metal Quality on heated nuggets — `IsMetalQualityTool`'s tag gate (`tag_Metal`/`tag_ToolBlank`/`tag_CopperSmall`/`tag_CopperBig`) never matched any real item (vanilla MetalNugget/MetalBarUnfinished carry no gameplay tags at all — the workshop's own InventoryFilter has to allowlist MetalNugget by exact UID for the same reason), so every strike just burned down vanilla's own inert "Strikes" stat with no quality payoff. Dropped the tag requirement; the existing active-"...Quality"-named SpecialDurability2 check is a sufficient, safe signal on its own.
- Fixed Natural Windflow going dead the moment fuel hits exactly 0, even in a confirmed-windy environment (e.g. High Grove) — the forge/workshop would just bleed heat via "Lose Temperature without fuel" (−100/hour) with no windflow rescue, contradicting the "wind carries it to smelting temp on its own" description. Natural Windflow's gate now requires the structure already be substantially hot (≥790°, reached only through real fuel-burning) instead of requiring live fuel — preserves the original fix (an unlit, never-fueled forge still can't self-heat from ambient wind alone) while letting wind carry an already-hot forge through fuel gaps as advertised.

### v1.10.1
- Fixed Blast-produced copper nuggets shipping at 0 quality (`SpawnService.Spawn` returns `null` on success in EA 0.65, so the old null-check on its return value always skipped the quality-init code; switched to the same ID-diff pattern already used by the Sluice/Grind/Fish paths).
- Reworded the Water-Driven Forge cooling description — the previous "cools −40°/hour when idle" line didn't match any reachable state of the forge's actual passive-effect stack.
- Added log breadcrumbs to 8 previously-silent `catch` blocks (diagnostic hygiene only — no behavior change).

### v1.10.0
- Added Cast Iron Rivet and Cast Iron Sheet (Metal Crafts tab) — WDI-native iron-tier fasteners forged from an iron-grade Metal Nugget/heated Metal Bar, always researchable, no ACT required. Renamed Cast Metal Sheet → Cast Copper Sheet (same UID; cosmetic rename now that an iron sibling exists).
- Rivets and nails are now fully cross-tier AND cross-mod interchangeable: every fastener slot (in either mod) accepts ACT Copper Nail, ACT Iron Nail, WDI Copper Rivet, and WDI Iron Rivet.
- Cast Copper/Iron Sheet are now cross-mod interchangeable with ACT's Copper/Iron Sheet, same tier only (a new pairing — the original v1.8.0 decoupling never wired sheet alternates).
- Framework's `BlueprintAlternates.AddAlternateIngredient` (2.17.0+) now accumulates multiple alternates per primary instead of overwriting the prior one — required for a single fastener slot to accept three or four interchangeable items.

### v1.8.3
- Removed a stale `CookingRecipes` entry ("Melt Iron Parts") on the Forge and Workshop that gated Iron Parts/Bearing/Axle/Wrench smelting at 1300° with the message "Temperature must reach 1300 to melt iron!" — left over from the pre-v1.7.1 design. Iron components have smelted at 1100° (same as copper) since v1.7.1 via their own passive-effect stat gate and `tag_SmeltsAt1100`; the stale recipe contradicted that (wrong on-screen message) and could double-count smelting charges above 1300°. Also removed the now-unused `tag_SmeltsAt1300` reference from both structures' inventory filters (no item in the mod ever carried it).
- Fixed the README understating the shipped blueprint count (said 18, actually 21 since v1.8.0's fastener additions).

### v1.8.0
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
