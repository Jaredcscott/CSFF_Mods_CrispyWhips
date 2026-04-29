# Advanced Copper Tools

**Quality of Life & Advanced Metalworking**
**Version:** 1.7.2
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.62d)

---

## Overview

Advanced Copper Tools turns copper (and other metals where it makes sense) into a tier of versatile crafting items focused on comfort, throughput, and light. Everything is loaded by [CSFFModFramework](https://github.com/jscott3/CSFF_Mods) — blueprints register themselves into the correct tabs automatically, perks land on the Situational tab, and every transform/refuel chain is JSON-driven where possible.

Major systems:

- **Metalworking basics** — Metal Sheets and Copper Nails forged from heated copper
- **Small Copper Stove** — Portable 2-slot fireplace; component for the bathtub and tea station
- **Wearable Metal Pan** — Multi-metal cookware that doubles as wearable equipment and a water-purifying pot
- **Large Copper Saw** — Two-handed saw that drops large trees in 1–2 hits via a Harmony bonus
- **Wheelbarrow** — Wearable cargo container that reduces effective weight
- **Copper Bathtub** — 3-state placed structure (empty / cold / warm) with deep cleansing and morale benefits
- **Metal Lantern** — 4-variant portable light (item × placed × lit × unlit) running on rendered oil
- **Oil chain** — Render animal fat (or hemp seed oil with H&F installed) into clean lamp oil; carry it in a Copper Oil Flask
- **Copper Tea Kettle** — Liquid container that boils water on any fire source
- **Tea Blending Station** — 3-variant kit / placed / lit workstation with six herb-and-grinding slots, a built-in 8-bowl water reservoir, passive drying, a "Grind All" action, and a heated reservoir while lit
- **Copper Chest** (formerly "Copper Pantry") — Sealed, animal-safe storage that slows spoilage to 20% of normal

---

## Crafting Tabs

Blueprints register into vanilla crafting tabs via `BlueprintTabs.json`:

| Tab | Blueprints |
|-----|-----------|
| **Survival → Support** | Rendered Oil, Render Hemp Seed Oil |
| **Metal & Clay → Metal Crafts** | Metal Sheet, Copper Nail, Forged Pan Blank, Wheel Rim, Wheel Hub (forged), Cast Wheel Hub, Cast Stove Top |
| **Construction → Advanced Tools** | Wearable Metal Pan, Large Saw, Lantern Oilwell, Metal Lantern, Copper Tea Kettle, Copper Oil Flask, Wheelbarrow Bucket, Wheelbarrow Handles, Wheel Assembly, Wheelbarrow |
| **Construction → Furniture** | Small Copper Stove, Copper Bathtub, Tea Blending Station, Copper Chest |

Time fields throughout this README use the standard CSFF unit: **1 tick = 15 minutes in-game**. `BuildingDaytimeCost` is the build time per stage (capped at 12 to keep stages ≤ 3 hours).

---

## Base Materials

| Item | Recipe | Build Time | Unlock |
|------|--------|-----------:|-------:|
| **Metal Sheet** | 3 heated copper nuggets + hammer (no spend) | 2 ticks | 8 ticks |
| **Copper Nail** | 1 heated copper nugget + hammer (no spend) | 2 ticks | 8 ticks |

Multi-metal supported wherever it makes sense — copper, ghost bronze, tin, tin bronze, and white bronze variants of metal sheets and pans all work through the same blueprints.

---

## Small Copper Stove

A portable fireplace with two cooking slots — burns longer than a campfire, generates ash and charcoal, can be picked up and placed anywhere.

| Component | Recipe | Time |
|-----------|--------|-----:|
| **Stove Top Mold** | Cast a stove top using a small molten crucible, 2 mud bricks, 2 clay molds | 3 ticks |
| **Cast Stove Top** | Smelted from a Stove Top Mold | (smelter) |
| **Small Copper Stove** | 4 metal sheets + 1 cast stove top + hammer | 5 ticks |

**Features**

- 2 cooking slots — cook food, heat liquids, boil water-pots
- 300-unit fuel capacity (oak +96 / twigs +24 / charcoal +8)
- Generates charcoal and ash while burning; loose `tag_FiresMedium` items burn safely on the stove top
- Acts as a heat source for any liquid container (kettle, wearable pan, clay bowl)
- Pick up / place anywhere; dismantle to recover sheets and stove top
- Doubles as a component for the Copper Bathtub blueprint

---

## Wearable Metal Pan

Multi-metal pan that you can wear, cook in, and use to purify water on a stove or campfire.

| Step | Recipe | Time |
|------|--------|-----:|
| **Forged Pan Blank** | 4 heated copper nuggets + hammer (no spend) | 3 ticks |
| **Wearable Metal Pan** | 1 shaped pan + rope + small leather + hammer (no spend) | 2 ticks |

**Features**

- Place on a stove or fire to cook food inside
- Holds 1200ml of liquid; boils to purify river/dirty water automatically
- Equip in a quiver-slot for hands-free transport
- Available in copper, ghost bronze, tin, tin bronze, and white bronze (forged separately)
- Dismantles back into the shaped pan blank

**Water purification**: place a filled wearable pan on any lit fire source. Heat propagates through the pan to the held liquid; vanilla water types run their own boil → safe-water transform.

---

## Large Copper Saw

A two-handled cutting tool that fells large trees substantially faster than the vanilla advanced axe.

- **Recipe:** 2 metal sheets + 2 planks + 4 copper nails (build 4 ticks, unlock 16 ticks)
- **Tags:** `tag_Cutter`, `tag_AdvancedAxe`-equivalent for tree-cutting interactions
- **Bonus:** A Harmony prefix on `GameManager.ActionRoutine` adds an extra **−25 Progress** when the saw is used on a large tree (pine, oak, birch, willow). Combined with the vanilla "Cut Tree" −25, that's −50 per swing — pine/willow fall in 1 swing, oak in 2 swings, birch in 2 (75 → 25 → 0).

The saw still works on small trees through the normal `tag_Axe` interaction; the bonus only applies to the four large-tree variants.

---

## Wheelbarrow

A wearable container that carries items at reduced effective weight. Built from four sub-assemblies plus the wheel pipeline.

| Component | Recipe | Time |
|-----------|--------|-----:|
| **Wheelbarrow Bucket** | 8 metal sheets + hammer (no spend) | 12 ticks |
| **Wheelbarrow Handles** | 3 planks + 2 small leather + 2 long sticks + sharp knife (no spend) | 3 ticks |
| **Wheel Rim** | 3 heated copper nuggets + hammer (no spend) | 3 ticks |
| **Wheel Hub (forged)** | 3 heated copper nuggets + hammer (no spend) | 3 ticks |
| **Wheel Hub (cast)** | 1 small molten crucible + 2 mud bricks + 2 clay molds | 3 ticks |
| **Wheel Assembly** | 1 wheel rim + 1 wheel hub + 1 rope + hammer (no spend) | 2 ticks |
| **Wheelbarrow** | bucket + handles + wheel assembly | 8 ticks |

**Features**

- Reduces stored item weight when worn
- Permanent — does not degrade from normal use
- Dismantles back to bucket, handles, and wheel assembly
- Bucket is also the load-bearing component of the Copper Bathtub

---

## Copper Bathtub

A 3-state placed structure for cleansing and morale.

**Recipe** (build 12 ticks, unlock 48 ticks): 1 wheelbarrow bucket + 1 small copper stove + 4 planks + 8 mud bricks + 1 large cloth + 4 long sticks + 3 small leather + hammer (not consumed).

**States**

- **Empty** — Place anywhere; pre-load fuel before filling. Fill from a water container or directly from the river.
- **Full (Cold)** — Take a cold bath for cleansing and a modest morale boost. Add firewood and light to heat the water.
- **Warm** — 96 max Heat (drains at −0.25/dtp ≈ ~1 day per fill). Take a warm bath for deep cleansing, major morale, spiritual boost, and body warmth.

**Actions**: Fill / Add Firewood / Light / Take Cold Bath / Take Warm Bath / Empty / Pick Up / Dismantle.

---

## Metal Lantern (4-variant pattern)

A portable light source with the standard CSFF four-variant pattern: item ↔ placed × lit ↔ unlit. Every transform between the four carries fuel forward.

| Variant | Card Type | Carryable | Drains? |
|---------|-----------|:---------:|:-------:|
| **Metal Lantern** (item, unlit) | 0 | yes | no (refuel here) |
| **Metal Lantern** (item, lit) | 0 | yes | yes (carried light) |
| **Placed Lantern** (unlit) | 2 | no | no (refuel here) |
| **Placed Lantern** (lit) | 2 | no | yes (area light) |

**Recipe** (build 2 ticks, unlock 6 ticks): 2 metal sheets + 1 lantern oilwell + 1 stone (forge component pattern).

**Fuel**: Pour `Oil` directly onto the unlit lantern, or drag a Copper Oil Flask onto it for one charge per drag. Holds 3 charges; each charge burns ~6 hours, total ~18 hours per full tank. Light by dragging a fire source onto the unlit lantern (gated by ≥10% fuel).

When fuel runs out, lit variants auto-extinguish back to their unlit counterpart in place.

---

## Oil Chain

`Oil` is a rendered animal-fat product that doubles as nutrition and lamp fuel.

| Blueprint | Recipe | Build | Unlock |
|-----------|--------|------:|------:|
| **Rendered Oil** | 2 animal fat + 1 clay bowl, over a fire | 4 ticks | 8 ticks |
| **Render Hemp Seed Oil** | 1 hemp seed oil → 1 oil (requires H&F installed)¹ | 4 ticks | 8 ticks |
| **Lantern Oilwell** | 1 metal sheet + 1 twine + hammer (no spend) | 2 ticks | 8 ticks |
| **Copper Oil Flask** | 2 metal sheets + 1 medium leather + hammer (no spend) | 3 ticks | 8 ticks |

The flask holds 6 charges — enough to fully refuel a lantern twice. Drag the flask onto a lantern to pour one charge.

¹ Render Hemp Seed Oil consumes a `herbs_fungi_hemp_seed_oil` card; if H&F is not installed, the blueprint registers but its ingredient cannot be produced.

---

## Copper Tea Kettle

A copper liquid container (build 3 ticks, unlock 16 ticks; 3 metal sheets + hammer) that boils water on any fire source.

- 200-unit Temperature; vanilla "Cool Down" passive dissipates heat off the source
- Place on a lit stove, campfire, or copper bathtub heat to boil
- Holds water; the held liquid runs its own boil → BoiledWater transform via `LiquidFuelValue`

---

## Tea Blending Station (3-variant)

A dedicated workbench with six herb-and-grinding slots, a built-in 8-bowl water reservoir, an integrated copper stove, and a Grind All action.

**Three variants** (kit → placed → lit). Every transform carries inventory, liquid, and fuel.

| Variant | Card Type | Pickable | Drains Fuel? |
|---------|-----------|:--------:|:------------:|
| **Tea Station Kit** | 0 (item) | yes | no |
| **Tea Station** (placed, unlit) | 2 | yes (must be empty) | no |
| **Tea Station** (placed, lit) | 2 | no — extinguish first | yes |

**Recipe** (build 4 ticks, unlock 16 ticks): 2 planks + 4 twine + 1 copper tea kettle + 1 small copper stove + 1 rotary quern + 15 stone (Stone). Place the kit to set up the workbench.

**Slots (×6)**: Place fresh herbs to dry passively over time. The drying recipe runs even unlit (`tag_Dryable` / `tag_DryableFastSpoilable`); lighting the stove speeds drying further and unlocks cooking, heating, and water-boiling recipes inside the slots.

**Grind All action**: One DismantleAction button reads each card's own Grind CardInteraction and produces every dried-or-millable item's ground variant. Powered by a Harmony prefix on `GameManager.ActionRoutine` / `PerformStackActionRoutine` — pure JSON could not implement this since `tag_Millable` items don't expose an OnFull transform.

**Reservoir (built-in 300-capacity)**: Drag clay bowls of water onto the station to fill it. Use "Draw Cold Water" with an empty bowl to extract cold water. While lit, the station heats its own held liquid via a Harmony per-tick patch (Water Temp 0 → 12, ~30 in-game minutes from cold), and you can use "Draw Hot Water" once the reservoir is at 50%+ heat.

**Light Fire / Extinguish / Pick Up**: Drag a fire source (gated by ≥10% fuel) to light. Extinguish via DismantleAction. Pick Up requires the reservoir empty and the station unlit; it transforms back to the kit with fuel and contents intact.

---

## Copper Chest

A sealed copper chest with thick insulated walls (build 10 ticks, unlock 32 ticks; 4 metal sheets + 4 planks + 6 copper nails + hammer).

- Slows spoilage on contained items to **20% of normal**
- **Animal-safe**: wildlife cannot raid this chest (lacks `tag_NotSafeFromAnimals`)
- Sealed inventory — items go in and out manually only

---

## Smelting Recovery

Every metal item is in `SmeltingRecipes.json` so you can melt it back down for nuggets:

| Item | Nuggets returned |
|------|-----------------:|
| Copper Nail | 1 |
| Wheel Hub / Pan Blank / Shaped Pan / Wearable Pan | 3–4 |
| Metal Sheet / Wheel Rim / Stove Top Mold / Cast Stove Top / Lantern Oilwell | 5–6 |
| Large Saw | 16 |
| Copper Tea Kettle | 18 |
| Copper Chest | 24 |
| Wheelbarrow Bucket | 48 |

All recipes use a duration of 8 ticks in the smelter.

---

## Character Perks

| Perk | Cost | Description |
|------|------|-------------|
| **Metal Pan** | 5 Suns | Start with copper lumps, hammer, wood, rope, leather, and a wearable metal pan equipped + a shaped pan in inventory. |
| **Wheelbarrow** | 3 Moons | Start with a fully assembled wheelbarrow. |
| **Wheelbarrow Kit** | 2 Moons | Start with bucket, handles, and wheel assembly + all wheelbarrow blueprints unlocked. |
| **Copper Bathtub** | 3 Moons | Start with a copper bathtub. |
| **Bathtub Kit** | 2 Moons | Start with all bathtub crafting materials + the bathtub blueprint unlocked. |
| **Large Saw** | 3 Moons | Start with a Large Copper Saw. |
| **Tea Blending Station** | 3 Moons | Start with a Tea Station Kit ready to place. |
| **Building Materials** | 4 Moons | Start with 10 planks, 10 leather, 10 long sticks, 20 mud bricks, 1 large cloth, and 1 spoon auger. |

All perks land on the Situational tab via the framework's perk injector.

---

## Harmony Patches

Three small Harmony patches handle gameplay logic that JSON alone can't express:

- **`SawEffectPatch`** — `GameManager.ActionRoutine` prefix; adds −25 Progress when the Large Saw is dragged onto one of the four large-tree GUIDs.
- **`TeaStationPatch`** — `GameManager.ActionRoutine` + `PerformStackActionRoutine` prefixes; resolves the "Grind All" DismantleAction for both unlit and lit station variants by reading each held card's own Grind CardInteraction and ejecting the ground results onto the board.
- **`HeatHeldLiquidPatch`** — `GameManager.Update` postfix; once per daytime point, bumps `LiquidFuelValue` on each lit Tea Station that holds liquid, counteracting the vanilla "Cool Down" passive on `LQ_Water` and reaching max temp in ~2 dtp.

All three are mod-scoped: they filter on this mod's UniqueIDs and never modify vanilla cards, drops, or stats.

---

## Installation

### Requirements

- BepInEx 5.x
- CSFFModFramework
- Card Survival: Fantasy Forest (EA 0.62d)

### Steps

1. Install BepInEx if not already installed.
2. Install CSFFModFramework in `BepInEx/plugins/CSFF_Mod_Framework/`.
3. Drop this mod folder at `BepInEx/plugins/Advanced_Copper_Tools/`.
4. Launch the game — content loads automatically; check `BepInEx/LogOutput.log` for `Advanced_Copper_Tools v1.7.2 loaded.`

### Deployed file structure

```
BepInEx/plugins/Advanced_Copper_Tools/
├── Advanced_Copper_Tools.dll
├── ModInfo.json
├── BlueprintTabs.json
├── SmeltingRecipes.json
├── CardData/
│   ├── Item/
│   ├── Blueprint/
│   └── Location/
├── CharacterPerk/
├── Localization/SimpEn.csv
└── Resource/Picture/
```

---

## Compatibility

- Works alongside HerbsAndFungi, WaterDrivenInfrastructure, RepeatAction, and other framework-based mods.
- Depends on CSFFModFramework for JSON loading, WarpData resolution, sprites, perks, and blueprint tab injection.
- The `Render Hemp Seed Oil` blueprint references an H&F card — without H&F installed, the recipe registers but its ingredient cannot be obtained.
- No vanilla items, drops, or stats are modified. Safe to add to existing saves; safe to remove (modded items disappear without corrupting the save).

---

## Troubleshooting

**Blueprints not appearing?** Verify CSFFModFramework is loaded — check `LogOutput.log` for `[CSFFModFramework]` lines and `Advanced_Copper_Tools v1.7.0 loaded.`

**Pan / kettle won't boil?** It must be on a *lit* fire source with fuel remaining. Vanilla water types boil via their own `LiquidFuelValue` OnFull transform; if the liquid isn't a heatable type, nothing happens.

**Stove fuel not depleting?** That's correct on the *unlit* stove. Once you light the stove (drag a fire source onto it), it consumes fuel at the standard rate and the fuel display reads as a percentage.

**Tea Station won't pick up?** The reservoir must be empty AND the stove must be unlit. Extinguish first, then drain water.

**Items show `[MISSING]` text?** `Localization/SimpEn.csv` is missing or corrupted — re-extract the mod folder.

---

## Credits

- **Author:** Jared (crispywhips)
- **Framework:** [CSFFModFramework](https://github.com/jscott3/CSFF_Mods) — handles JSON loading, WarpData resolution, sprites, perk injection, blueprint tab injection, and ProducedCards normalization
- **Tooling:** BepInEx + Harmony
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games
