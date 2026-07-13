# Advanced Copper Tools

**Quality of Life & Advanced Metalworking**
**Version:** 1.11.7
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.65)

---

## Overview

Advanced Copper Tools turns copper (and other metals where it makes sense) into a tier of versatile crafting items focused on comfort, throughput, and light. Everything is loaded by [CSFFModFramework](https://github.com/jscott3/CSFF_Mods) — blueprints register themselves into the correct tabs automatically, perks land on the Situational tab, and every transform/refuel chain is JSON-driven where possible.

Major systems:

- **Metalworking basics** — Metal Sheets and Copper Nails forged from heated copper
- **Small Copper Stove** — Portable 2-slot fireplace; component for the bathtub and tea station
- **Wearable Metal Pan** — Multi-metal cookware that doubles as wearable equipment and a water-purifying pot
- **Large Copper Saw** — Two-handed saw that drops large trees in 1–2 hits via a Harmony bonus
- **Copper Armor Set** — Helmet, bracers, greaves, and torso armor crafted from copper sheets and nails
- **Wheelbarrow** — Wearable cargo container that reduces effective weight
- **Copper Bathtub** — 3-state placed structure (empty / cold / warm) with deep cleansing and morale benefits
- **Metal Lantern** — 4-variant portable light (item × placed × lit × unlit) running on rendered oil
- **Oil chain** — Render animal fat (or hemp seed oil with H&F installed) into clean lamp oil; carry it in a Copper Oil Flask
- **Copper Tea Kettle** — Liquid container that boils water on any fire source
- **Copper Cauldron** — Fire-placeable batch vessel with six cooking slots and a 6240 ml basin
- **Tea Blending Station** — 3-variant kit / placed / lit workstation with six herb-and-grinding slots, a built-in 8-bowl water reservoir, passive drying, a "Grind All" action, and a heated reservoir while lit
- **Copper Chest** (formerly "Copper Pantry") — Sealed, animal-safe storage that slows spoilage to 20% of normal

---

## Crafting Tabs

Blueprints register into vanilla crafting tabs via `BlueprintTabs.json`:

| Tab | Blueprints |
|-----|-----------|
| **Survival → Support** | Rendered Oil, Render Hemp Seed Oil |
| **Survival → Fire** | Copper Brazier |
| **Metal & Clay → Metal Crafts** | Metal Sheet, Copper Nail, Forged Pan Blank, Wheel Rim, Wheel Hub (forged), Cast Wheel Hub, Cast Stove Top |
| **Construction → Metal Tools** | Wearable Metal Pan, Large Saw, Lantern Oilwell, Copper Tea Kettle, Copper Oil Flask, Copper Cauldron, Copper Helmet, Copper Bracers, Copper Greaves, Copper Armor |
| **Construction → Advanced Tools** | Metal Lantern, Wheelbarrow Bucket, Wheelbarrow Handles, Wheel Assembly, Wheelbarrow |
| **Construction → Furniture** | Small Copper Stove, Copper Bathtub, Tea Blending Station, Copper Chest, Copper Brazier |

Time fields throughout this README use the standard CSFF unit: **1 tick = 15 minutes in-game**. `BuildingDaytimeCost` is the build time per stage (capped at 12 to keep stages ≤ 3 hours).

---

## Base Materials

| Item | Recipe | Build Time | Unlock |
|------|--------|-----------:|-------:|
| **Metal Sheet** | 1 heated metal bar (copper-grade only) + hammer (no spend) | 2 ticks | 16 ticks |
| **Copper Nail** | 1 heated copper nugget + hammer (no spend) | 2 ticks | 16 ticks |

Multi-metal supported for pans — copper, ghost bronze, tin, tin bronze, and white bronze variants of the Wearable Metal Pan chain forge from whichever metal-grade nugget you use. Metal Sheets are copper-specific: the Metal Sheet blueprint's Heated Metal Bar requirement is gated to copper-grade bars only, even though the item's own data carries name/art variants for the other four metals.

---

## Small Copper Stove

A portable fireplace with two cooking slots — burns longer than a campfire, generates ash and charcoal, can be picked up and placed anywhere.

| Component | Recipe | Time |
|-----------|--------|-----:|
| **Stove Top Mold** | Cast a stove top using a small molten crucible, 2 mud bricks, 2 clay molds | 3 ticks |
| **Cast Stove Top** | Smelted from a Stove Top Mold | (smelter) |
| **Small Copper Stove** | 4 metal sheets + 1 cast stove top + hammer (no spend) + 1 Tin Solder | 5 ticks |

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
| **Wearable Metal Pan** | 1 shaped pan + 1 plank + 1 rope + small leather + 1 copper nail + hammer (no spend) + 1 Tin Solder | 2 ticks |

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

- **Recipe:** 2 metal sheets + 2 Wood + 4 copper nails + hammer (no spend) + 1 Tin Solder (build 4 ticks, unlock 32 ticks)
- **Tags:** `tag_Axe`, `tag_AdvancedAxe`
- **Bonus:** A Harmony prefix on `GameManager.ActionRoutine` adds an extra **−25 Progress** when the saw is used on a large tree (pine, oak, birch, willow). Combined with the vanilla "Cut Tree" −25, that's −50 per swing — pine/willow fall in 1 swing, oak in 2 swings, birch in 2 (75 → 25 → 0).

The saw still works on small trees through the normal `tag_Axe` interaction; the bonus only applies to the four large-tree variants.

---

## Copper Armor Set

Wearable copper armor pieces protect specific body zones when equipped. Each piece uses the new armor artwork in `Resource/Picture/` and can be dismantled back into its spent materials.

| Piece | Protection | Recipe | Build | Unlock |
|-------|------------|--------|------:|-------:|
| **Copper Helmet** | Head +30 | 2 metal sheets + 4 copper nails + 1 small leather + 2 sinew + hammer (no spend) + 1 Tin Solder | 4 ticks | 24 ticks |
| **Copper Bracers** | Arms +15 each | 2 heated metal lumps + 4 copper nails + 1 small leather + 2 sinew + hammer (no spend) + 1 Tin Solder | 4 ticks | 16 ticks |
| **Copper Greaves** | Legs +15 each | 2 heated metal lumps + 4 copper nails + 1 small leather + 2 sinew + hammer (no spend) + 1 Tin Solder | 4 ticks | 24 ticks |
| **Copper Armor** | Torso +30 | 4 metal sheets + 4 copper nails + 1 medium leather + 4 sinew + hammer (no spend) + 1 Tin Solder | 6 ticks | 32 ticks |

Helmet and Armor are research-gated by having a Metal Sheet; Bracers and Greaves are gated by a Heated Metal Lump. All four appear under **Construction → Metal Tools**. At full durability, the copper set's durability multiplier raises these values enough to fill the Armor stat when replacing the leather cuirass, helmet, greaves, and bracers while keeping the usual gloves, shoes, tunic, and trousers equipped.

---

## Wheelbarrow

A wearable container that carries items at reduced effective weight. Built from four sub-assemblies plus the wheel pipeline.

| Component | Recipe | Time |
|-----------|--------|-----:|
| **Wheelbarrow Bucket** | 8 metal sheets + hammer (no spend) + 1 Tin Solder | 12 ticks |
| **Wheelbarrow Handles** | 3 planks + 2 small leather + 2 long sticks + 4 copper nails + sharp knife (no spend) + hammer (no spend) | 3 ticks |
| **Wheel Rim** | 1 heated metal bar + hammer (no spend) | 3 ticks |
| **Wheel Hub (forged)** | 3 heated copper nuggets + hammer (no spend) | 3 ticks |
| **Wheel Hub (cast)** | 1 small molten crucible + 2 mud bricks + 2 clay molds | 3 ticks |
| **Wheel Assembly** | 1 wheel rim + 1 wheel hub + 1 wood + hammer (no spend) + 1 Tin Solder | 2 ticks |
| **Wheelbarrow** | bucket + handles + wheel assembly + 1 Tin Solder | 8 ticks |

**Features**

- Reduces stored item weight when worn
- Permanent — does not degrade from normal use
- Dismantles back to bucket, handles, and wheel assembly
- Bucket is also the load-bearing component of the Copper Bathtub

---

## Copper Bathtub

A 3-state placed structure for cleansing and morale.

**Recipe** (build 12 ticks, unlock 96 ticks): 1 wheelbarrow bucket + 1 small copper stove + 4 planks + 8 mud bricks + 1 large cloth + 4 long sticks + 3 small leather + hammer (not consumed) + 1 Tin Solder.

**States**

- **Empty** — Place anywhere; pre-load fuel before filling. Fill from a water container or directly from the river.
- **Full (Cold)** — Take a cold bath for cleansing and a modest morale boost. Add firewood and light to heat the water.
- **Warm** — 24 max Heat (drains at −0.25/dtp ≈ ~1 day per fill). Take a warm bath for deep cleansing, major morale, spiritual boost, and body warmth.

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

**Recipe** (build 2 ticks, unlock 12 ticks): 2 metal sheets + 1 lantern oilwell + 1 stone + 1 Tin Solder (forge component pattern).

**Fuel**: Pour `Oil` directly onto the unlit lantern, or drag a Copper Oil Flask onto it for one charge per drag. Holds 3 charges; each charge burns ~6 hours, total ~18 hours per full tank. Light by dragging a fire source onto the unlit lantern (gated by ≥10% fuel).

When fuel runs out, lit variants auto-extinguish back to their unlit counterpart in place.

---

## Oil Chain

`Oil` is a rendered animal-fat product that doubles as nutrition and lamp fuel.

| Blueprint | Recipe | Build | Unlock |
|-----------|--------|------:|------:|
| **Rendered Oil** | 2 animal fat + 1 clay bowl, over a fire | 4 ticks | 16 ticks |
| **Render Hemp Seed Oil** | 1 hemp seed oil → 1 oil (requires H&F installed)¹ | 4 ticks | 16 ticks |
| **Lantern Oilwell** | 1 metal sheet + 1 twine + hammer (no spend) + 1 Tin Solder | 2 ticks | 16 ticks |
| **Copper Oil Flask** | 2 metal sheets + 1 medium leather + hammer (no spend) + 1 Tin Solder | 3 ticks | 16 ticks |

The flask holds 6 charges — enough to fully refuel a lantern twice. Drag the flask onto a lantern to pour one charge.

¹ Render Hemp Seed Oil consumes a `herbs_fungi_hemp_seed_oil` card; if H&F is not installed, the blueprint registers but its ingredient cannot be produced.

---

## Copper Tea Kettle

A copper liquid container (build 3 ticks, unlock 32 ticks; 3 metal sheets + hammer + 1 Tin Solder) that boils water on any fire source.

- 200-unit Temperature; vanilla "Cool Down" passive dissipates heat off the source
- Place on a lit stove, campfire, or copper bathtub heat to boil
- Holds water; the held liquid runs its own boil → BoiledWater transform via `LiquidFuelValue`

---

## Copper Cauldron

A large portable cooking vessel for batch cooking and brewing. Place the cauldron into a lit campfire, fireplace, fire pit, stove, or other fire source to heat it like the vanilla clay cauldron.

**Recipe** (build 6 ticks, unlock 48 ticks): 5 metal sheets + 4 copper nails + hammer (not consumed) + 1 Tin Solder.

**Features**

- Six ingredient slots for batch cooking multiple `tag_Cookable` or `tag_Boilable` items
- 6240 ml open basin; boil/brew recipes require liquid in the cauldron
- Accepted by vanilla-style fire inventories through cooking-container tags
- Cools down when removed from heat, matching vanilla cooking containers

---

## Tea Blending Station (3-variant)

A dedicated workbench with six herb-and-grinding slots, a built-in 8-bowl water reservoir, an integrated copper stove, and a Grind All action.

**Three variants** (kit → placed → lit). Every transform carries inventory, liquid, and fuel.

| Variant | Card Type | Pickable | Drains Fuel? |
|---------|-----------|:--------:|:------------:|
| **Tea Station Kit** | 0 (item) | yes | no |
| **Tea Station** (placed, unlit) | 2 | yes (must be empty) | no |
| **Tea Station** (placed, lit) | 2 | no — extinguish first | yes |

**Recipe** (build 4 ticks, unlock 64 ticks): 2 planks + 4 twine + 1 copper tea kettle + 1 small copper stove + 1 rotary quern + 15 stone (Stone). Place the kit to set up the workbench.

**Slots (×6)**: Place fresh herbs to dry passively over time. The drying recipe runs even unlit (`tag_Dryable` / `tag_DryableFastSpoilable`); lighting the stove speeds drying further and unlocks cooking, heating, and water-boiling recipes inside the slots.

**Grind All action**: One DismantleAction button reads each card's own Grind CardInteraction and produces every dried-or-millable item's ground variant. Powered by a Harmony prefix on `GameManager.ActionRoutine` / `PerformStackActionRoutine` — pure JSON could not implement this since `tag_Millable` items don't expose an OnFull transform.

**Reservoir (built-in 300-capacity)**: Drag clay bowls of water onto the station to fill it. Use "Draw Cold Water" with an empty bowl to extract cold water. While lit, the station heats its own held liquid via a Harmony per-tick patch (Water Temp 0 → 12, ~30 in-game minutes from cold), and you can use "Draw Hot Water" once the reservoir is at 50%+ heat.

**Light Fire / Extinguish / Pick Up**: Drag a fire source (gated by ≥10% fuel) to light. Extinguish via DismantleAction. Pick Up requires the reservoir empty and the station unlit; it transforms back to the kit with fuel and contents intact.

---

## Copper Brazier

A copper fire-bowl on a stick tripod (build 4 ticks, unlock 16 ticks; 3 metal sheets + 4 copper nails + 3 long sticks + 1 Tin Solder). Gated by having a Metal Sheet.

- **3 states:** Kit (CT0, carryable) → Placed unlit (CT2) → Placed lit (CT2)
- **Fuel:** Drag rendered oil onto the placed brazier to fill (24 units per clay bowl); drag a fire source to light
- **Light:** Lit variant provides warm-toned light; drains oil at 1.5 per daytime point (≈ 64 DTP per fill)
- **Pack Up:** Extinguish first (or pack up the unlit version) — remaining oil transfers back to the kit
- **Smelting:** Can be melted in the furnace for 22 copper nuggets

Blueprinted under **Construction → Furniture** and **Survival → Fire**.

---

## Copper Chest

A sealed copper chest with thick insulated walls (build 10 ticks, unlock 64 ticks; 4 metal sheets + 4 planks + 6 copper nails + hammer + 1 Tin Solder).

- Slows spoilage on contained items to **20% of normal**
- **Animal-safe**: wildlife cannot raid this chest (lacks `tag_NotSafeFromAnimals`)
- Sealed inventory — items go in and out manually only

---

## Smelting Recovery

All crafted metal items can be melted back down for nuggets in the furnace (the Copper Oil Flask is the sole exception — its leather binding is not recovered):

| Item | Nuggets returned |
|------|-----------------:|
| Copper Nail | 1 |
| Shaped Metal Pan Head | 4 |
| Wearable Metal Pan | 5 |
| Metal Sheet / Wheel Hub / Wheel Rim / Stove Top Mold / Cast Stove Top / Lantern Oilwell | 6 |
| Wheel Assembly | 12 |
| Large Saw | 16 |
| Metal Lantern | 18 |
| Copper Tea Kettle | 18 |
| Copper Bracers | 11 |
| Copper Helmet | 15 |
| Copper Greaves | 15 |
| Copper Armor | 23 |
| Copper Brazier | 22 |
| Small Copper Stove / Copper Chest | 30 |
| Copper Cauldron | 34 |
| Tea Station Kit / Wheelbarrow Bucket | 48 |
| Copper Bathtub | 78 |

All recipes use a duration of 8 ticks in the smelter.

---

## Cave System

ACT adds three cave environments east of the Waterfall Caves on the world map. The Tin Vein Cave is the entry point, accessible by travelling from Waterfall Caves.

| Cave | Contents | Notes |
|------|----------|-------|
| **Tin Vein Cave** | 3 × Tin Vein | Hub — enter via Waterfall Caves to the west; connects north to Copper Vein Cave, south to Iron Vein Cave |
| **Copper Vein Cave** | 3 × Copper Vein | North of Tin Vein Cave |
| **Iron Vein Cave** | 3 × Iron Vein | South of Tin Vein Cave |

Each vein supports a **Mine** action (pickaxe, 3 ticks / ~45 min) and a **Chip Away** action (axe, hammer, shovel, or antler, 8 ticks / 2 hours). Mining with a pickaxe yields 1–2 ore per strike; chipping away yields 1 per strike and costs more tool durability.

**Ore yields and uses:**
- **Copper Vein** → Greenstone (1–2 per strike). Smelt in any forge/furnace for vanilla copper nuggets.
- **Iron Vein** → Bog Iron (1–2 per strike). Smelt via the standard vanilla path for wrought iron. Finding Bog Iron also unlocks the **Forge Iron Nails** blueprint (requires iron-grade metal nuggets; Metal Crafts tab).
- **Tin Vein** → Tin Ore (1–2 per strike). Smelt in any forge/furnace to produce a **tin-grade metal nugget** (vanilla metal nugget, tin type). Use tin-grade nuggets to forge **Tin Solder** via the **Forge Tin Solder** blueprint (Metal Crafts tab, unlocked by possessing Tin Ore; 1 tin-grade nugget yields 2 Tin Solder).

**Tin Solder is a hard dependency for most of the mod.** Nearly every finished ACT blueprint beyond the raw-material tier (Metal Sheet, Copper Nail, Forged Pan Blank, Cast Stove Top, Wheel Hub, Wheel Rim) additionally requires 1 Tin Solder: Small Copper Stove, Wearable Metal Pan, Large Copper Saw, all four copper armor pieces, every Wheelbarrow sub-assembly, Copper Bathtub, Metal Lantern, Lantern Oilwell, Copper Oil Flask, Copper Tea Kettle, Copper Cauldron, Copper Brazier, and Copper Chest. In practice, reaching the Tin Vein Cave (via Waterfall Caves) is required before most of this mod's content becomes buildable.

---

## Character Perks

| Perk | Cost | Description |
|------|------|-------------|
| **Metal Pan** | 5 Suns | Start with a forge hammer, leather bellows, 5 metal nuggets, wood, rope, and small leather, plus a shaped pan head and two wearable metal pans (one equipped). Also unlocks the Pan Blank, Wearable Metal Pan, Metal Sheet, Cast Stove Top, and Small Copper Stove blueprints. |
| **Wheelbarrow** | 3 Moons | Start with a fully assembled wheelbarrow. |
| **Wheelbarrow Kit** | 2 Moons | Start with bucket, handles, and wheel assembly. |
| **Copper Bathtub** | 1 Moon | Start with a copper bathtub. |
| **Bathtub Kit** | 1 Moon | Start with all bathtub crafting materials: bucket, copper stove, 4 planks, 8 mud bricks, 1 large cloth, 4 long sticks, and 3 leather. |
| **Large Saw** | 45 Suns | Start with a Large Copper Saw. |
| **Tea Blending Station** | 2 Moons | Start with a Tea Station Kit ready to place. |
| **Building Materials** | 2 Moons | Start with 10 planks, 10 small leather, 10 long sticks, 20 mud bricks, 1 large cloth, and 1 spoon auger. |

All perks land on the Situational tab via the framework's perk injector.

---

## Harmony Patches

Several Harmony/ActionRouter hooks handle gameplay logic that JSON alone can't express, with one opt-in compatibility fallback:

- **`SawEffectPatch`** — `GameManager.ActionRoutine` / `CardOnCardActionRoutine` prefix; adds −25 Progress when the Large Saw is dragged onto one of the four large-tree GUIDs.
- **`TeaStationPatch`** — `GameManager.ActionRoutine`, `CardOnCardActionRoutine`, and `PerformStackActionRoutine` hooks; resolves "Grind All" for both station variants and applies the targeted `Draw Boiled Water` fix after JSON fills the bowl, so spawned water is actually hot and one reservoir charge is consumed.
- **`GameLoadPatch`** — `LoadMainGameData` postfix that repairs a missing `ArmorValueDurabilitiesMultiplier` entry on each copper armor piece and makes iron nails an accepted alternate for copper nails in every blueprint/improvement slot. It also prefixes `EncounterPopup.GenerateAndApplyPlayerWound` and subscribes to `GameManager.OnGMInitialized` to keep equipped and inventory copper armor registered in the game's combat armor list, including after a save is loaded.
- **`IronNailSmeltPatch`** — ActionRouter hook on Tin Ore smelting; tags the resulting Metal Nugget with the correct metal-type stat (SpecialDurability4) so downstream blueprint metal-type gates recognize it as tin-grade.
- **`IronVeinQualityPatch`** — ActionRouter hook on the Iron Vein; sets a Quality stat on vein-mined Dried Bog Iron (which skips the vanilla fresh-to-dried aging step) so it's immediately usable at a reasonable quality.
- **`HeatHeldLiquidPatch`** — disabled by default; enable `Compatibility.EnableLegacyStationLiquidHeater` only for beta/testing layouts where a lit Tea Station stores real liquid on the station card. Current Tea Stations use Water Temp / Water Charges stats instead.

These hooks are mod-scoped and filter on this mod's UniqueIDs. The exception is `VanillaFireKettlePatch` (invoked from `GameLoadPatch`), which modifies vanilla fire cards to accept ACT containers — this is disclosed in §Compatibility and is safe, scoped, and idempotent.

---

## Installation

### Requirements

- BepInEx 5.x
- CSFFModFramework
- Card Survival: Fantasy Forest (EA 0.65)

### Steps

1. Install BepInEx if not already installed.
2. Install CSFFModFramework in `BepInEx/plugins/CSFF_Mod_Framework/`.
3. Drop this mod folder at `BepInEx/plugins/Advanced_Copper_Tools/`.
4. Launch the game — content loads automatically; check `BepInEx/LogOutput.log` for `Advanced_Copper_Tools v1.11.7 loaded.`

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
- Declares HerbsAndFungi as a soft dependency so the optional hemp-oil recipe loads after H&F when it is installed.
- The `Render Hemp Seed Oil` blueprint references an H&F card — without H&F installed, the recipe registers but its ingredient cannot be obtained.
- ACT modifies vanilla fire cards (campfire, fireplace, fire pit, etc.) to accept ACT containers — this is what `VanillaFireKettlePatch` does, and it is scoped, idempotent, and safe. ACT does not modify vanilla drops, stats, or any other vanilla card data. Safe to add to existing saves; safe to remove (modded items disappear without corrupting the save).

### Depended on by

Other in-house mods build directly on top of ACT's content:

- **WaterDrivenInfrastructure** — optional compatibility. WDI is fully playable without ACT, but if ACT is installed it accepts ACT Copper Nails, Tin Solder, and Copper Sheet interchangeably with its own fasteners and uses ACT outputs from matching Workshop actions.
- **Community Mod Chest** — functional dependency for three features: the River Bridge improvement (unlocks the Village area), the Market Stall blueprint (consumes a Copper Pantry/Copper Chest), and the Village Academy's Armorer course (hidden entirely if ACT isn't installed).

---

## Troubleshooting

**Blueprints not appearing?** Verify CSFFModFramework is loaded — check `LogOutput.log` for `[CSFFModFramework]` lines and `Advanced_Copper_Tools v1.11.7 loaded.`

**Pan / kettle won't boil?** It must be on a *lit* fire source with fuel remaining. Vanilla water types boil via their own `LiquidFuelValue` OnFull transform; if the liquid isn't a heatable type, nothing happens.

**Stove fuel not depleting?** That's correct on the *unlit* stove. Once you light the stove (drag a fire source onto it), it consumes fuel at the standard rate and the fuel display reads as a percentage.

**Tea Station won't pick up?** The reservoir must be empty AND the stove must be unlit. Extinguish first, then drain water.

**Items show `[MISSING]` text?** `Localization/SimpEn.csv` is missing or corrupted — re-extract the mod folder.

---

## Version History

### v1.11.5
- Internal refactor only: `PatchNailInterchangeability` (iron/copper nail interchangeability) now delegates to the framework's new shared `CSFFModFramework.Api.BlueprintAlternates.AddAlternateIngredient` helper instead of a locally duplicated reflection block. No behavior change — same blueprint slots accept the same alternates as before. Done as part of removing WaterDrivenInfrastructure's hard dependency on this mod, which needed the same pattern.

### v1.11.4
- Fixed a near-complete 2× duplicate key set in both `Localization/SimpEn.csv` and `SimpCn.csv` (left over from a prior translation revision never being cleaned up). Because the localization loader is last-wins, several duplicate rows had gone stale or corrupted and were silently active: the Copper Stove's placed help text and the Tea Station Kit / Copper Pantry descriptions were truncated mid-sentence, the Tea Station's "Dry Herbs (Accelerated...)" action labels had an unclosed parenthesis, the Metal Lantern's help text had dropped the "light it with a fire source" step, the Large Saw's blueprint description omitted copper nails from its own recipe, and one Chinese entry (Copper Stove placed help text) had been overwritten with a stray fragment of English text ("twigs"). All corrected; no recipe or mechanic changes.
- Corrected the **Metal Pan** perk's README description, which had gone stale — it undersold what the perk actually grants (a forge hammer, leather bellows, 5 metal nuggets, wood, rope, small leather, a shaped pan head, two wearable metal pans, and 5 blueprint unlocks). No change to the perk itself; documentation only.

### v1.11.0
- New **Cave Prospector** perk (★1 star, ±0 difficulty): an opt-in challenge trait. Without it the cave network is reachable as before; with it, the passages from Tin Cave to the Copper and Iron caves start as **Collapsed Rock Face** barriers. Dig each one through (once) with a pickaxe or shovel to permanently open the passage — and recover some loose stone. Non-perk characters are unaffected (save-safe).
- The Cave Prospector perk icon and the Collapsed Rock Face cards reuse the existing tin vein art.

### v1.10.1
- Cave system: Copper Vein now yields Greenstone (vanilla copper ore), Iron Vein yields Bog Iron (vanilla), Tin Vein yields ACT Tin Ore
- Tin Ore smelts in any forge/furnace to produce a tin-grade metal nugget; **Forge Tin Solder** blueprint (Metal Crafts tab) lets you convert tin-grade nuggets into Tin Solder
- **Forge Iron Nails** blueprint added (Metal Crafts tab): unlocked by having Bog Iron; produces 4 Iron Nails from iron-grade metal nuggets + hammer
- Iron Nails are stronger than copper nails and interchangeable with them in all construction blueprints
- Custom vein art now correctly displayed (was pointing at vanilla sprites)
- Perk leather (Building Materials, Bathtub Kit, Metal Pan) description now notes raw leather must be tanned before use in blueprints
- Removed global vanilla leather stat mutation that was causing all small leather to spawn pre-tanned

### v1.9.0
- Tier 2 framework retrofit: ActionRouter/SpawnService integration; manifest cleanup (removed `ModLoaderVerison`/`ModEditorVersion` fields)
- Perk descriptions corrected: Wheelbarrow Kit and Bathtub Kit no longer claim blueprint unlocks that aren't implemented; Building Materials now says "10 small leather"
- Copper Armor set perk descriptions fixed; IDEAS.md cleaned of shipped content

### v1.8.0
- Added **Copper Brazier** — 3-variant oil-burning fire bowl (kit → placed unlit → placed lit)
  - Fueled by rendered oil (24 units per clay bowl); lit variant drains at 1.5 per DTP (~64 DTP per fill)
  - Pack-up transfers remaining oil back to the kit; smelts for 22 copper nuggets
  - Blueprinted under Construction → Furniture, gated by having a Metal Sheet

### v1.7.8
- Version bump for release; `CopperHelmet` image reference corrected (was `CopperHemlet`); unlock time README values reconciled with JSON (16 ticks for Metal Sheet and Copper Nail)

### v1.7.7
- EA 0.63f compatibility; blueprint tab injector updated to use live UI tabs (fixes journal tab disappearing on EA 0.63f)
- `StartUnlocked` / `ConstantlyChecking` fields corrected across all operation blueprints

### v1.7.6
- CardData JSON fixes for smelting and WarpData resolution; copper item smelting pattern aligned to Progress-based passive smelting

### v1.7.5
- EA 0.63 compatibility pass
- `HeatHeldLiquidPatch` disabled by default; Tea Station uses Water Temp / Water Charges stats (legacy liquid-on-station layout removed)
- Startup log normalized to single Info line per CSFF mod logging norms
- `TeaStationPatch`: draw-boiled-water fix applies one reservoir charge on spawn so output is hot

### v1.7.4
- Copper Chest (formerly Copper Pantry): 20% spoilage rate, animal-safe (no `tag_NotSafeFromAnimals`), 4 sheets + 4 planks + 6 nails recipe
- Tea Blending Station: 8-bowl water reservoir with Draw Cold Water / Draw Boiled Water actions
- Grind All action reads each slot card's own Grind CI — no `tag_Millable` gate required

### v1.7.1
- Tea Blending Station v1: 3-variant kit/placed/lit, 6 herb-drying slots, passive drying recipe, integrated copper stove
- Copper Cauldron: 6 cooking slots, 6240 ml basin

### v1.6.x
- Copper Bathtub: 3-state empty/cold/warm with deep cleansing and morale bonuses
- Metal Lantern: 4-variant portable light (item × placed × lit × unlit) with rendered oil fuel
- Oil chain: rendered animal fat → lamp oil; Copper Oil Flask for transport

### v1.5.x
- Wheelbarrow: 4-sub-assembly wearable cargo container with weight reduction
- Copper Tea Kettle: boils water on any fire source

### v1.4.x
- Small Copper Stove: portable 2-slot fireplace
- Wearable Metal Pan: multi-metal, wearable, water-purifying
- Large Copper Saw: −25 Progress Harmony bonus on large trees

### v1.0.x
- Initial release: Metal Sheets, Copper Nails, basic metalworking blueprints

---

## Credits

- **Author:** Jared (crispywhips)
- **Framework:** [CSFFModFramework](https://github.com/jscott3/CSFF_Mods) — handles JSON loading, WarpData resolution, sprites, perk injection, blueprint tab injection, and ProducedCards normalization
- **Tooling:** BepInEx + Harmony
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games
