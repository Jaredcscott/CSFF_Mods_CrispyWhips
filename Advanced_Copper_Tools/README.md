# Advanced Copper Tools

**Quality of Life & Advanced Metalworking**
**Version:** 1.16.10
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
- **Copper Bathtub** — 3-state placed structure (empty / cold / warm) with deep cleansing and morale benefits; the warm state now splits into a lukewarm **Warm Bath** and a piping-hot **Hot Bath** tier (≥50% heat) with a bigger mood boost and a genuine Stress reduction
- **Metal Lantern** — 4-variant portable light (item × placed × lit × unlit) running on rendered oil
- **Oil chain** — Render animal fat (or hemp seed oil with H&F installed) into clean lamp oil; carry it in a Copper Oil Flask (now tracks copper-through-white-bronze metal type)
- **Copper Tea Kettle** - Liquid container that boils water on a lit fire or stove, on any fire with room for its weight (a full kettle needs a Fireplace, Fire Pit or Oven; a Campfire takes it about half full)
- **Copper Cauldron** - Fire-placeable batch vessel with a 3000-weight cooking basket and a 6240 ml basin; weighs 1200 like the vanilla clay cauldron, so it needs a Fireplace or Fire Pit (now tracks copper-through-white-bronze metal type)
- **Tea Blending Station** — 3-variant kit / placed / lit workstation with six herb-and-grinding slots, a built-in 8-bowl water reservoir, passive drying, a "Grind All" action, a heated reservoir while lit, and three brewable herbal teas (Calming from willow bark, Warming from wild garlic, Focus from spirit mushrooms)
- **Copper Chest** (formerly "Copper Pantry") — Sealed, animal-safe storage that slows spoilage to 20% of normal
- **Iron-Grade Armor** — Iron Sheet forged from iron nuggets feeds a tougher iron helmet/bracers/greaves/armor tier with higher Armor Values and durability than copper
- **Copper Watering Can** — Fill from any shallow water source; carry it for a quick drink, or empty it out when you're done
- **Copper-Rim Chamberpot** — Minor hygiene item; small mood bonus for four uses before it needs emptying

---

## Crafting Tabs

Blueprints register into vanilla crafting tabs via `BlueprintTabs.json`:

| Tab | Blueprints |
|-----|-----------|
| **Survival → Support** | Rendered Oil, Rendered Fish Oil, Render Hemp Seed Oil |
| **Survival → Fire** | Copper Brazier |
| **Metal & Clay → Metal Crafts** | Metal Sheet, Copper Nail, Forged Pan Blank, Wheel Rim, Wheel Hub (forged), Cast Wheel Hub, Cast Stove Top, Iron Sheet, Bronze Sheet |
| **Construction → Metal Tools** | Wearable Metal Pan, Large Saw, Lantern Oilwell, Copper Tea Kettle, Copper Oil Flask, Copper Cauldron, Copper Helmet, Copper Bracers, Copper Greaves, Copper Armor, Copper Watering Can, Iron Helmet, Iron Bracers, Iron Greaves, Iron Armor (Male), Iron Armor (Female) |
| **Construction → Advanced Tools** | Metal Lantern, Wheelbarrow Bucket, Wheelbarrow Handles, Wheel Assembly, Wheelbarrow |
| **Construction → Furniture** | Small Copper Stove, Copper Bathtub, Tea Blending Station, Copper Chest, Copper Brazier, Copper-Rim Chamberpot |

Time fields throughout this README use the standard CSFF unit: **1 tick = 15 minutes in-game**. `BuildingDaytimeCost` is the build time per stage (capped at 12 to keep stages ≤ 3 hours).

---

## Base Materials

| Item | Recipe | Build Time | Unlock |
|------|--------|-----------:|-------:|
| **Metal Sheet** | 1 heated metal bar (copper-grade only) + hammer (no spend) | 2 ticks | 16 ticks |
| **Bronze Sheet** | 1 heated bronze-grade metal bar (Ghost Bronze, Tin Bronze, or White Bronze) + hammer (no spend) | 2 ticks | 16 ticks |
| **Copper Nail** | 1 heated copper nugget + hammer (no spend) | 2 ticks | 16 ticks |

Multi-metal supported for pans and sheets alike. The Wearable Metal Pan chain forges copper, ghost bronze, tin, tin bronze, and white bronze variants from whichever metal-grade nugget you use. Metal Sheets work the same way but through two separate blueprints: the base **Metal Sheet** blueprint only accepts a copper-grade bar (Metal Type 100) and produces a plain Copper Sheet, while the **Bronze Sheet** blueprint (added alongside the Bronze Armor Set below) accepts a bronze-grade bar (Ghost Bronze/Tin Bronze/White Bronze, Metal Type 110-140) and produces the same underlying sheet card, correctly tagged with its bronze-grade Metal Type. Both come out of the same item, just via different recipes - the Bronze Armor Set's gate checks that Metal Type stat directly.

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
- A recruited Partner NPC can be assigned to tend its fuel under the vanilla Fire keeping duty (confirmed in-game 2026-08-15)

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

Helmet and Armor are research-gated by having a Metal Sheet; Bracers and Greaves are gated by a Heated Metal Lump. All four appear under **Construction → Metal Tools**. Armor value is flat regardless of condition: the values in the table above are what you'll see in combat at any durability.

---

## Bronze Armor Set

A mid-tier armor set slotting between Copper and Iron, using the same forged-armor pattern. Each piece requires bronze-grade Metal Sheets, produced via the separate **Bronze Sheet** blueprint (Metal & Clay → Metal Crafts, see Base Materials above): hammer a heated Ghost Bronze, Tin Bronze, or White Bronze metal bar (Metal Type 110-140) instead of a copper-grade one. A plain Copper Sheet (Metal Type 100, from the base Metal Sheet blueprint) does not satisfy this gate.

Art note: Bronze Helmet/Bracers/Greaves/Armor and their blueprints currently reuse the Copper set's sprites (`CopperHelmet`/`CopperArmor`/`CopperBracers`/`CopperGreaves`) - visually identical to Copper armor on the board until distinct Bronze art ships.

| Piece | Protection | Recipe | Build | Unlock |
|-------|------------|--------|------:|-------:|
| **Bronze Helmet** | Head +37.5 | 2 bronze-grade metal sheets + 4 copper nails + 1 small leather + 2 sinew + hammer (no spend) + 1 Tin Solder | 4 ticks | 28 ticks |
| **Bronze Bracers** | Arms +17.5 each | 2 bronze-grade metal sheets + 4 copper nails + 1 small leather + 2 sinew + hammer (no spend) + 1 Tin Solder | 4 ticks | 28 ticks |
| **Bronze Greaves** | Legs +17.5 each | 2 bronze-grade metal sheets + 4 copper nails + 1 small leather + 2 sinew + hammer (no spend) + 1 Tin Solder | 4 ticks | 28 ticks |
| **Bronze Armor** | Torso +37.5 | 4 bronze-grade metal sheets + 4 copper nails + 1 medium leather + 4 sinew + hammer (no spend) + 1 Tin Solder | 6 ticks | 32 ticks |

All four appear under **Construction → Metal Tools** alongside the Copper and Iron sets. Durability is 120 (vs. Copper's 100 and Iron's 140), furnace-recyclable like every other ACT armor piece.

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

**Actions**: Fill / Add Firewood / Light / Take Cold Bath / Take Warm Bath / Take Hot Bath (≥50% heat) / Wash with Soap (drag in vanilla Soap for a quick clean and mood boost that doesn't need the fire lit) / Empty / Pick Up / Dismantle.

---

## Metal Lantern (4-variant pattern)

A portable light source with the standard CSFF four-variant pattern: item ↔ placed × lit ↔ unlit. Every transform between the four carries fuel forward.

| Variant | Card Type | Carryable | Drains? |
|---------|-----------|:---------:|:-------:|
| **Metal Lantern** (item, unlit) | 0 | yes | no (refuel here) |
| **Metal Lantern** (item, lit) | 0 | yes | yes (carried light) |
| **Placed Lantern** (unlit) | 2 | no | no (refuel here) |
| **Placed Lantern** (lit) | 2 | no | yes (area light) |

**Recipe** (build 2 ticks, unlock 16 ticks): 2 metal sheets + 1 lantern oilwell + 1 stone + 1 Tin Solder (forge component pattern).

**Fuel**: Pour `Oil` directly onto the unlit lantern, or drag a Copper Oil Flask onto it for one charge per drag. Holds 3 charges; each charge burns ~6 hours, total ~18 hours per full tank. Light by dragging a fire source onto the unlit lantern (gated by ≥10% fuel).

When fuel runs out, lit variants auto-extinguish back to their unlit counterpart in place.

A recruited Partner NPC can be assigned to tend a placed lantern's fuel under the vanilla Fire keeping duty (confirmed in-game 2026-08-15); the Ownership panel restricting who may refuel it is not yet confirmed working.

---

## Oil Chain

`Oil` is a rendered animal-fat product that doubles as nutrition and lamp fuel.

| Blueprint | Recipe | Build | Unlock |
|-----------|--------|------:|------:|
| **Rendered Oil** | 2 animal fat + 1 clay bowl, over a fire | 4 ticks | 16 ticks |
| **Rendered Fish Oil** | 2 fatty fish meat + 1 clay bowl, over a fire | 4 ticks | 16 ticks |
| **Render Hemp Seed Oil** | 1 hemp seed oil → 1 oil (requires H&F installed)¹ | 4 ticks | 16 ticks |
| **Lantern Oilwell** | 1 metal sheet + 1 twine + hammer (no spend) + 1 Tin Solder | 2 ticks | 16 ticks |
| **Copper Oil Flask** | 2 metal sheets + 1 medium leather + hammer (no spend) + 1 Tin Solder | 3 ticks | 16 ticks |

The flask holds 6 charges — enough to fully refuel a lantern twice. Drag the flask onto a lantern to pour one charge.

¹ Render Hemp Seed Oil consumes a `herbs_fungi_hemp_seed_oil` card; if H&F is not installed, the blueprint registers but its ingredient cannot be produced.

---

## Copper Tea Kettle

A copper liquid container (build 3 ticks, unlock 32 ticks; 3 metal sheets + hammer + 1 Tin Solder) that boils water on a lit fire or stove.

- 200-unit Temperature; vanilla "Cool Down" passive dissipates heat off the source
- Place on a lit stove, fire, or copper bathtub heat to boil
- Fits a vanilla fire the way vanilla pots do, by weight against the fire's own capacity: the kettle weighs 180 and its water counts, so a full kettle (about 970) fits a Fireplace (1200), Fire Pit (2400) or Oven (1200), while a Campfire (600) takes it only about half full. The Copper Stove holds it at any fill
- Holds water; the held liquid runs its own boil → BoiledWater transform via `LiquidFuelValue`

---

## Copper Cauldron

A large portable cooking vessel for batch cooking and brewing. Place the cauldron into a lit fireplace or fire pit to heat it like the vanilla clay cauldron. It weighs 1200, the same as the clay cauldron, so it follows the same fire rules: one fits a Fireplace, two fit a Fire Pit, a Campfire is too small, and the Oven refuses it as it refuses the clay cauldron.

**Recipe** (build 6 ticks, unlock 48 ticks): 5 metal sheets + 4 copper nails + hammer (not consumed) + 1 Tin Solder.

**Features**

- Weight-limited ingredient basket (3000 capacity) for batch cooking `tag_Cookable` or `tag_Boilable` items; how many fit depends on their weight, not on a fixed slot count
- 6240 ml open basin; boil/brew recipes require liquid in the cauldron
- Accepted by vanilla fire inventories by name (the fire accepts this card, not a tag), so no vanilla item gains a new place to go
- Cools down when removed from heat, matching vanilla cooking containers

---

## Tea Blending Station (3-variant)

A dedicated workbench with six herb-and-grinding slots, a built-in 8-bowl water reservoir, an integrated copper stove, and a Grind All action.

**Three variants** (kit → placed → lit). Every transform carries liquid and fuel. Lighting and putting out the station also carry whatever sits in its six slots; picking it up does not, because the kit has no slots, so anything still in them is set down beside you.

| Variant | Card Type | Pickable | Drains Fuel? |
|---------|-----------|:--------:|:------------:|
| **Tea Station Kit** | 0 (item) | yes | no |
| **Tea Station** (placed, unlit) | 2 | yes (slot contents are set down) | no |
| **Tea Station** (placed, lit) | 2 | no — extinguish first | yes |

**Recipe** (build 4 ticks, unlock 64 ticks): 2 planks + 4 twine + 1 copper tea kettle + 1 small copper stove + 1 rotary quern + 15 stone (Stone). Place the kit to set up the workbench.

**Slots (×6)**: Place fresh herbs to dry passively over time. The drying recipe runs even unlit (`tag_Dryable` / `tag_DryableFastSpoilable`); lighting the stove speeds drying further and unlocks cooking, heating, and water-boiling recipes inside the slots.

**Grind All action**: One DismantleAction button reads each card's own Grind CardInteraction and produces every dried-or-millable item's ground variant. Powered by a Harmony prefix on `GameManager.ActionRoutine` / `PerformStackActionRoutine` — pure JSON could not implement this since `tag_Millable` items don't expose an OnFull transform.

**Reservoir (built-in 8-charge Water Charges tank)**: Drag clay bowls of water onto the station to fill it. Use "Draw Cold Water" with an empty bowl to extract cold water. While lit, the station heats its own held liquid via a Harmony per-tick patch (Water Temp 0 → 12, ~30 in-game minutes from cold), and you can use "Draw Hot Water" once the reservoir is at 50%+ heat.

**Light Fire / Extinguish / Pick Up**: Drag a fire source (gated by ≥10% fuel) to light. Extinguish via DismantleAction. Pick Up is offered only on the unlit station; it transforms back to the kit, and anything in the six slots is set down beside you rather than carried.

---

## Copper Brazier

A copper fire-bowl on a stick tripod (build 4 ticks, unlock 16 ticks; 3 metal sheets + 4 copper nails + 3 long sticks + 1 Tin Solder). Gated by having a Metal Sheet.

- **3 states:** Kit (CT0, carryable) → Placed unlit (CT2) → Placed lit (CT2)
- **Fuel:** Drag rendered oil onto the placed brazier to fill (24 units per clay bowl); drag a fire source to light
- **Light:** Lit variant provides warm-toned light; drains oil at 1.5 per daytime point (≈ 64 DTP per fill)
- **Pack Up:** Extinguish first (or pack up the unlit version) — remaining oil transfers back to the kit
- **Smelting:** Can be melted in the furnace for 22 copper nuggets
- **Partner duty:** a recruited Partner NPC can be assigned to tend its fuel under the vanilla Fire keeping duty (confirmed in-game 2026-08-15); the Ownership panel restricting who may refuel it is not yet confirmed working

Blueprinted under **Construction → Furniture** and **Survival → Fire**.

---

## Copper Chest

A sealed copper chest with thick insulated walls (build 10 ticks, unlock 64 ticks; 4 metal sheets + 4 planks + 6 copper nails + hammer + 1 Tin Solder).

- Slows spoilage on contained items to **20% of normal**
- **Animal-safe**: wildlife cannot raid this chest (lacks `tag_NotSafeFromAnimals`)
- Sealed inventory — items go in and out manually only

---

## Ore Chest

A high-capacity, animal-safe sealed metal crate built for hauling the cave network's bulk raws — Greenstone, Bog Iron, Tin Ore, Salt, and Stone (build 10 ticks, unlock 64 ticks; 6 metal sheets + 4 planks + 8 copper nails + hammer + 1 Tin Solder). Food fits too, but gains nothing here: the Ore Chest has no spoilage protection, so keep perishables in the Copper Chest.

- 6000 weight capacity (item) / 15000 weight capacity (placed) — no spoilage protection, just space
- **Animal-safe**: wildlife cannot raid this crate
- Appears under **Construction → Furniture**
- Art note: reuses the Copper Chest sprite (`Copper_Chest`) - a distinct Ore Chest look is not yet shipped.

---

## Salt-Cured Meat

Raw meat packed in Salt and left to cure into a slow-spoiling travel ration (1 raw meat + 2 Salt; build 2 ticks, unlock 8 ticks; Survival → Support). Gives the Salt Mine a self-contained ACT payoff beyond feeding vanilla cooking — the salt content also leaves you thirstier than a plain cooked meat would.

---

## Smelting Recovery

All crafted metal items can be melted back down for nuggets in the furnace (the Copper Oil Flask is the sole exception — its leather binding is not recovered):

| Item | Nuggets returned |
|------|-----------------:|
| Copper Nail | 1 |
| Shaped Metal Pan Head | 4 |
| Wearable Metal Pan / Iron Sheet | 5 |
| Metal Sheet / Wheel Hub / Wheel Rim / Stove Top Mold / Cast Stove Top / Lantern Oilwell / Copper-Rim Chamberpot | 6 |
| Copper Watering Can | 10 |
| Copper Bracers | 11 |
| Wheel Assembly | 12 |
| Bronze Bracers | 13 |
| Copper Helmet / Copper Greaves / Iron Bracers | 15 |
| Large Saw | 16 |
| Bronze Helmet / Bronze Greaves | 17 |
| Metal Lantern / Copper Tea Kettle | 18 |
| Iron Helmet / Iron Greaves | 20 |
| Copper Brazier | 22 |
| Copper Armor | 23 |
| Bronze Armor | 26 |
| Small Copper Stove / Copper Chest / Iron Armor (Male) / Iron Armor (Female) | 30 |
| Copper Cauldron | 34 |
| Tea Station Kit / Wheelbarrow Bucket | 48 |
| Copper Bathtub | 78 |

All recipes use a duration of 8 ticks in the smelter.

---

## Cave System

ACT adds five cave environments east of the Waterfall Caves on the world map. The Tin Vein Cave (Metal Mines) is the entry point, accessible by travelling from Waterfall Caves.

| Cave | Contents | Notes |
|------|----------|-------|
| **Tin Vein Cave** (Metal Mines) | 3 × Tin Vein | Hub — enter via Waterfall Caves to the west; connects north to Copper Vein Cave, south to Iron Vein Cave, east to Rock Quarry |
| **Copper Vein Cave** | 3 × Copper Vein | North of Tin Vein Cave; connects east to Salt Mine |
| **Iron Vein Cave** | 3 × Iron Vein | South of Tin Vein Cave |
| **Salt Mine** | 3 × Salt Vein | East of Copper Vein Cave; connects south to Rock Quarry |
| **Rock Quarry** | 3 × Rock Vein | East of Tin Vein Cave; connects north to Salt Mine |

Each vein supports a **Mine** action (pickaxe, 3 ticks / ~45 min) and a **Chip Away** action (axe, hammer, shovel, or antler, 8 ticks / 2 hours). Mining with a pickaxe yields 1–2 ore per strike; chipping away yields 1 per strike and costs more tool durability. (Rock Vein is the exception — see below.)

**Ore yields and uses:**
- **Copper Vein** → Greenstone (1–2 per strike). Smelt in any forge/furnace for vanilla copper nuggets.
- **Iron Vein** → Bog Iron (1–2 per strike). Smelt via the standard vanilla path for wrought iron. Finding Bog Iron also unlocks the **Forge Iron Nails** blueprint (requires iron-grade metal nuggets; Metal Crafts tab).
- **Tin Vein** → Tin Ore (1–2 per strike). Smelt in any forge/furnace to produce a **tin-grade metal nugget** (vanilla metal nugget, tin type). Use tin-grade nuggets to forge **Tin Solder** via the **Forge Tin Solder** blueprint (Metal Crafts tab, unlocked by possessing Tin Ore; 1 tin-grade nugget yields 2 Tin Solder).
- **Salt Vein** → vanilla Salt (1–2 per strike).
- **Rock Vein** → vanilla Stone and Heavy Stone in bulk. Both the **Mine Stone** (pickaxe) and **Chip Away** actions yield a guaranteed 3 Stone + 3 Heavy Stone per strike. 6 strikes before depleting (same as every other vein), for a total of 18 Stone + 18 Heavy Stone per vein.

**Tin Solder is a hard dependency for most of the mod.** Nearly every finished ACT blueprint beyond the raw-material tier (Metal Sheet, Copper Nail, Forged Pan Blank, Cast Stove Top, Wheel Hub, Wheel Rim) additionally requires 1 Tin Solder: Small Copper Stove, Wearable Metal Pan, Large Copper Saw, all four copper armor pieces, every Wheelbarrow sub-assembly, Copper Bathtub, Metal Lantern, Lantern Oilwell, Copper Oil Flask, Copper Tea Kettle, Copper Cauldron, Copper Brazier, and Copper Chest. In practice, reaching the Tin Vein Cave (via Waterfall Caves) is required before most of this mod's content becomes buildable. If WaterDrivenInfrastructure is installed, its Alloy Solder is accepted as a substitute everywhere Tin Solder is required (v1.15.3+).

---

## Character Perks

| Perk | Cost | Description |
|------|------|-------------|
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
- **`GameLoadPatch`** — `LoadMainGameData` postfix that makes iron nails an accepted alternate for copper nails in every blueprint/improvement slot (and Tin Solder interchangeable with WDI's Alloy Solder), and lets ACT's Copper/Iron Sheet slots also accept WaterDrivenInfrastructure's matching Cast Sheet - tier-locked, so iron-tier armor still needs iron-tier material, and a no-op when WDI is not installed. It also prefixes `EncounterPopup.GenerateAndApplyPlayerWound` and subscribes to `GameManager.OnGMInitialized` to keep equipped and inventory ACT armor registered in the game's combat armor list, including after a save is loaded.
- **`TinOreSmeltPatch`** — ActionRouter hook on Tin Ore smelting; tags the resulting Metal Nugget with the correct metal-type stat (SpecialDurability4) so downstream blueprint metal-type gates recognize it as tin-grade, and carries the ore's own Quality across to the nugget's Metal Quality (SpecialDurability2).
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
4. Launch the game — content loads automatically; check `BepInEx/LogOutput.log` for `Advanced_Copper_Tools v1.16.10 loaded.`

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
- ACT changes vanilla fire cards (Campfire, Fireplace, Fire Pit, their extinguished forms, Hearth, Awakened Hearth, lit Oven) only to make room for its two copper containers: the copper kettle and copper cauldron are added to the fire's accepted cards, and a heating recipe for those two cards is appended after the fire's own recipes. The lit Oven takes the kettle only and lists the copper cauldron among the cards it refuses, exactly as vanilla lists the clay cauldron there. That is all `VanillaFireKettlePatch` does. It never changes a fire's weight capacity, never touches the Sauna Stove, and never widens a fire to accept or heat vanilla items (up to 1.16.9 it raised every one of these fires, and the Sauna Stove, to 2580 capacity; fixed in 1.16.10). ACT does not modify vanilla drops, stats, or any other vanilla card data. Safe to add to existing saves; safe to remove (modded items disappear without corrupting the save).

### Depended on by

Other in-house mods build directly on top of ACT's content:

- **WaterDrivenInfrastructure** — optional compatibility. WDI is fully playable without ACT, but if ACT is installed it accepts ACT Copper Nails, Tin Solder, and Copper Sheet interchangeably with its own fasteners and uses ACT outputs from matching Workshop actions. The reverse also holds (v1.15.3+): ACT's iron-tier armor accepts WDI's Copper/Iron Rivets alongside Copper Nail, and any ACT recipe requiring Tin Solder also accepts WDI's Alloy Solder.
- **Community Mod Chest** — functional dependency for three features: the River Bridge improvement (unlocks the Village area), the Market Stall blueprint (consumes a Copper Pantry/Copper Chest), and the Village Academy's Armorer course (hidden entirely if ACT isn't installed).

---

## Troubleshooting

**Blueprints not appearing?** Verify CSFFModFramework is loaded — check `LogOutput.log` for `[CSFFModFramework]` lines and `Advanced_Copper_Tools v1.16.10 loaded.`

**Pan / kettle won't boil?** It must be on a *lit* fire source with fuel remaining. Vanilla water types boil via their own `LiquidFuelValue` OnFull transform; if the liquid isn't a heatable type, nothing happens.

**Stove fuel not depleting?** That's correct on the *unlit* stove. Once you light the stove (drag a fire source onto it), it consumes fuel at the standard rate and the fuel display reads as a percentage.

**Tea Station won't pick up?** The stove must be unlit: Pick Up only appears on the unlit station, so extinguish it first. Anything left in the six slots is set down beside you when you pick it up.

**Items show `[MISSING]` text?** `Localization/SimpEn.csv` is missing or corrupted — re-extract the mod folder.

---

## Version History

### v1.16.10 (current)
- **Vanilla fires keep their own capacities again.** ACT had raised every fire it touches, and the
  Sauna Stove, to 2580; the Campfire, Fireplace, Fire Pit and Oven are back to 600, 1200, 2400 and
  1200, and the Sauna Stove holds nothing, as in vanilla. Reported by Chiwei.
- **Copper Cauldron weighs 1200 like the clay cauldron**, so it needs a Fireplace or Fire Pit and
  stays out of the Oven. A full Copper Tea Kettle needs a Fireplace, Fire Pit or Oven.
- **Alloy Metal Sheets can be picked out of a pile** with the pile's expand toggle, as vanilla metal
  nuggets can. Reported by Chiwei.

### v1.16.7
- **Download cut from 37.2 MB to 11.8 MB.** Every card image had been packaged twice, and the three
  Copper Bathtub images shipped at 2400x1792 instead of the 512-wide size the rest of the mod uses.
  No gameplay, content or balance change.

### v1.16.5
- **Bronze/White-Bronze armor is now actually craftable as designed**: fixed a bad field-name bug that left the Bronze recipes' metal-type gate permanently inactive, added a new Bronze Sheet blueprint as the only way to produce a sheet that satisfies it, and closed a matching gap in the four Bronze blueprints' research-unlock gate (previously satisfied by any Metal Sheet). See CHANGELOG.md for full detail.
- Corrected several player-facing honesty issues: armor no longer claimed to track its build metal, the bathtub's Wash with Soap claim is now reworded everywhere it appears (not just ModInfo.json), the Metal Sheet and Bronze Armor Set sections no longer contradict each other, a false [1.16.4] armor-scaling fix claim was corrected, undisclosed Bronze/Ore Chest art reuse is now noted, and the README no longer advertises the "Metal Pan" perk removed in [1.16.4].
- Removed the non-functional armor quality-scaling scaffolding (dead `ArmorValueDurabilitiesMultiplier`/`EffectScalesWithDurabilities` blocks and the C# code that kept restoring them) instead of leaving it disclosed as broken; fixed Bronze Armor's leather-grade mismatch; filled in 8 missing Smelting Recovery rows.

### v1.16.2
- **Removed the Cave Prospector perk.** The Collapsed Rock Face gates it described have applied to every character by default since v1.15.6 — the perk itself controlled nothing and, unlike Forest Scout in HerbsAndFungi, was never given a compensating stat bonus, so its 1-Star cost bought no mechanical effect. The cave network (portal + dig-through access) is unchanged.

### v1.16.1
- Fixed blank card art on the Rendered Fish Oil and Salt-Cured Meat blueprints (bad sprite references); both now use vanilla art matching their descriptions.

### v1.16.0
- Added Ore Chest (high-capacity bulk-raws storage), Salt-Cured Meat (slow-spoiling ration), Bronze/White-Bronze armor tier (Helmet/Bracers/Greaves/Armor), and a Wash with Soap bathtub interaction. See CHANGELOG.md for full detail.

### v1.15.9
- Internal: Copper Stove, Copper Brazier, and Metal Lantern (lit and unlit variants of each) are now marked compatible with the vanilla Firekeeping duty, so a recruited Partner NPC can potentially be assigned to tend their fuel. Update 2026-09-08: confirmed in-game - a recruited Partner does tend all three stations' fuel under the vanilla Fire keeping duty. The Ownership panel on the Copper Brazier and Metal Lantern, which restricts who may refuel them, is still unconfirmed; the Copper Stove has no ownership panel (it's communal).

### v1.15.8
- **Powder ground at the Tea Station no longer vanishes when poured into bottles, cloth bags, or wooden barrels** (Nexus bug report, 2026-08-10). The Grind All in-place transform was resetting the pour quantity powders carry to 0, so the game's built-in pour-into-container action destroyed the powder card and added no liquid. Note: powder ground before this update still carries the zero quantity in your save — grind fresh material after updating.
- **Tin Ore can now be smelted in any smelting container**, not only iron-capable ones — it was incorrectly gated on `tag_SmeltingContainerIron` instead of the general smelting tag every other smeltable item uses.

### v1.15.7
- Documentation review for publish; no functional changes. README/ModInfo verified up to date against all shipped content through v1.15.6 (Salt Mine/Rock Quarry, bathtub Hot Bath tier, iron armor Male/Female split, tin solder/nail cross-mod interchangeability, and related fixes) — version bump only.

### v1.15.6
- **Collapsed Rock Face walls now spawn for every player by default**, not only after equipping
  the Cave Prospector perk. The perk was never required to dig through a wall — that's gated on
  tool tags (pickaxe/shovel/axe/antler/knife) on the wall's own interaction — it only controlled
  whether the wall existed at all. Requires `CSFFModFramework` 2.21.1+.

### v1.15.3
- Iron-tier armor fastener slots now also accept Copper Nail and both WaterDrivenInfrastructure rivets (previously only Iron Nail) — cross-tier fastener interchangeability is now bidirectional.
- New Tin Solder ↔ WaterDrivenInfrastructure Alloy Solder interchangeability.
- Copper Watering Can's description/help text/README no longer advertise the unimplemented "douse fires" and "pour into any water container" interactions — corrected to describe what it actually does (Fill from Water, Drink, Empty Out).
- Tea Blending Station's README reservoir description corrected to "8-charge Water Charges tank" (was incorrectly stated as "300-capacity").
- Fixed Oil.json's card art reference (was pointing at a nonexistent sprite name and rendering blank).
- Calming/Focus/Warming Tea's spoilage stat relabeled "Spoilage" (was "Freshness").

### v1.15.2
- **Rock Vein yield is now deterministic: 3 Stone + 3 Heavy Stone per strike, guaranteed**, on both the Mine Stone (pickaxe) and Chip Away actions — replacing the previous mixed guaranteed/chance drop table. Every Rock Vein is now worth exactly 18 Stone + 18 Heavy Stone regardless of which tool is used.

### v1.15.1
- Clearing a Collapsed Rock Face now recovers 3 Heavy Stone (up from 1) alongside the existing 3 Stone, across all six tunnels (Copper, Tin, Iron, Salt, Salt↔Quarry, Quarry).
- Rock Vein now also yields Heavy Stone (50% per pickaxe strike, 25% per chip-away strike), matching the Collapsed Rock Face tunnels. It already shared the 6-strike depletion count with every other vein.

### v1.15.0
- Added two new cave locations: **Salt Mine** (east of Copper Vein Cave, 3 × Salt Vein) and **Rock Quarry** (east of Tin Vein Cave, 3 × Rock Vein, bulk stone), connected to each other north–south. Both new passages use the same Collapsed Rock Face / Cave Prospector perk gating as the existing network.

### v1.14.3
- Bathtub pick-up now preserves invested firewood, water level, ash, and charcoal progress instead of silently discarding it; a picked-up Warm tub stays a Warm kit instead of degrading to cold. Also fixed a pre-existing bug where the declared "transfer charcoal progress" JSON key didn't match a real game field and silently did nothing.
- Iron Sheet's research-discovery gate now actually requires an iron-grade nugget (previously fired on any metal nugget).
- Collapsed Rock Face (Copper/Iron Vein Caves) now uses the vanilla rockfall sprite instead of tin-vein art.

### v1.14.2
- Small Copper Stove now has an NPC trading value (1500), anchored to vanilla copper goods.

### v1.14.1
- **Iron Armor now offers separate Male/Female torso variants** (`IronArmor_M.png` / `IronArmor_F.png`), matching the body-model split other armor already respects. Same protection (Torso +45) and recipe as before — art only.

### v1.14.0
- Fixed Iron Sheet's card art: it referenced the now-removed `IronSheet.png` sprite (replaced by the image-upgrade pass's `MetalSheet_Iron.png`) on both the item and its blueprint — was silently falling back to a missing-sprite placeholder in-game.
- **Copper/Iron Sheet now interchangeable with WaterDrivenInfrastructure's Cast Copper/Iron Sheet** (same tier only — Iron Sheet only pairs with Cast Iron Sheet) when WDI is installed. Nails were already cross-mod/cross-tier interchangeable with WDI's rivets (v1.11.5 and earlier); this closes the equivalent gap for sheets, and additionally makes Copper Nail accept WDI's new Iron Rivet. Soft dependency — no behavior change when WDI isn't installed.

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
- **Thanks to Chiwei**, a player whose report behind 1.16.10 caught ACT overwriting the vanilla fire capacities (and giving the Sauna Stove an inventory) and alloy Metal Sheets that could not be picked out of a pile
- **Framework:** [CSFFModFramework](https://github.com/jscott3/CSFF_Mods) — handles JSON loading, WarpData resolution, sprites, perk injection, blueprint tab injection, and ProducedCards normalization
- **Tooling:** BepInEx + Harmony
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games
