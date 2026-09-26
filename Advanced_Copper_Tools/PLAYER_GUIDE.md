# Advanced Copper Tools — Player Guide

A practical how-to for every item in the mod: how to make it, what it does, and how to use it.

---

## Getting Started

Before anything else, smelt copper and get a hammer. Every recipe in this mod flows from those two things. Suggested build order:

1. **Metal Sheet + Copper Nail** (base materials)
2. **Small Copper Stove** (better campfire with ash/charcoal output)
3. **Wearable Metal Pan** (portable cooking + water purification)
4. **Copper Tea Kettle** (boiling water on a fire or stove)
5. **Large Copper Saw** (faster wood supply)
6. **Copper Armor Set** (helmet, bracers, greaves, torso armor)
7. **Metal Lantern + Oil Chain** (portable night light)
8. **Wheelbarrow** (haul more weight)
9. **Copper Cauldron** (batch cooking)
10. **Copper Bathtub** (deep cleansing + morale)
11. **Tea Blending Station** (herb processing hub)
12. **Copper Chest** (long-term sealed storage)

---

## Metal Sheet & Copper Nail

The building blocks of everything else in the mod.

**To craft:** Heat the metal in a forge or fire until it glows, then use a hammer on it.

| Item | Inputs | Build Time |
|------|--------|:----------:|
| **Metal Sheet** | 1 heated copper-grade metal bar + hammer (not consumed) | 2 ticks |
| **Bronze Sheet** | 1 heated bronze-grade metal bar (Ghost Bronze, Tin Bronze or White Bronze) + hammer (not consumed) | 2 ticks |
| **Copper Nail** | 1 heated copper-grade nugget + hammer (not consumed) | 2 ticks |

**Sheet metal types:** both sheet recipes make the same Metal Sheet card, named for the metal it was hammered from (Copper Sheet, Ghost Bronze Sheet and so on). The Bronze Armor Set needs a bronze-grade sheet, so it matters which one you pick up. Sheets of different metals pile together; use the pile's expand toggle to spread them out and drag the one you want. Copper Nails are hammered from copper-grade nuggets only and carry no metal type, so every nail is the same.

**Smelting back:** Most metal items melt back down to nuggets in a hot forge; see Smelting Recovery below.

---

## Small Copper Stove

A portable fireplace with two cooking slots. Burns longer than a campfire and produces charcoal and ash.

**Recipe:** 4 metal sheets + 1 Stove Top + hammer (not consumed) + 1 Tin Solder. The Stove Top is cast from a Stove Top Mold, made from 2 mud bricks + 2 clay with a Small Molten Crucible (the crucible is not consumed).

**How to use:**
1. Place the stove anywhere (outdoors or in a sheltered location).
2. Add firewood, twigs, or charcoal as fuel. Fuel capacity: 300 units (oak +96, charcoal +8, twigs +24).
3. Drag a fire source (tinder, torch, another fire) onto the stove to light it.
4. Place food items or liquid containers directly on the two cooking slots.
5. The stove generates charcoal and ash during burning — collect them for other uses.

**Pick up:** Extinguish first, then use the Pick Up action. You can carry it to a new location and place it again.

**Used as a component:** The Small Copper Stove is required in the Copper Bathtub blueprint.

---

## Wearable Metal Pan

A multi-metal frying pan that you can wear, cook in, and use to purify water.

**Recipe:**
1. Hammer 4 metal nuggets into a **Shaped Metal Pan Head** (hammer not consumed).
2. Combine the pan head + 1 rope + 1 plank + 1 small leather + 1 copper nail + hammer (not consumed) + 1 Tin Solder → **Wearable Metal Pan**.

**Cooking:** Place the pan on any lit fire source. Put food items in the pan to cook them directly.

**Water purification:** Fill the pan with river or dirty water, then place it on a lit fire. Heat propagates through the pan to the held liquid, and the vanilla boil-to-safe-water transform runs automatically.

**Wearing it:** Equip in a quiver-style slot for hands-free transport — the pan travels with you without taking up inventory space.

**Available metals:** Copper, ghost bronze, tin, tin bronze, and white bronze. One blueprint covers them all: the pan takes the metal of the nuggets you hammer, and piles of pans or pan heads of different metals have the expand toggle so you can pick one out.

**To recover:** Dismantle the wearable pan to get back the shaped pan head and the parts that went into it.

---

## Large Copper Saw

A two-handed saw that fells large trees significantly faster than any vanilla axe.

**Recipe:** 2 metal sheets + 2 wood + 4 copper nails + hammer (not consumed) + 1 Tin Solder.

**How to use:** Drag the saw onto a tree (just like any cutting tool). On large trees — pine, oak, birch, willow — the saw adds an extra **−25 Progress** on top of the vanilla −25, for a total of −50 per swing. Most large trees fall in 1–2 swings.

**Small trees:** Works normally through the standard `tag_Axe` tree-cutting interaction; no extra bonus on small trees.

**Tags:** `tag_Cutter`, treated as an advanced axe for all tree interactions.

---

## Copper Armor Set

Four copper armor pieces can be equipped for body-zone protection. They are crafted in **Construction → Metal Tools**; the Helmet and Armor unlock once you have a Metal Sheet, the Bracers and Greaves once you have a Heated Metal Bar.

| Piece | Protects | Requirements |
|-------|----------|--------------|
| **Copper Helmet** | Head +30 | 2 metal sheets + 4 copper nails + 1 small leather + 2 sinew + hammer + 1 Tin Solder |
| **Copper Bracers** | Arms +15 each | 2 heated metal bars + 4 copper nails + 1 small leather + 2 sinew + hammer + 1 Tin Solder |
| **Copper Greaves** | Legs +15 each | 2 heated metal bars + 4 copper nails + 1 small leather + 2 sinew + hammer + 1 Tin Solder |
| **Copper Armor** | Torso +30 | 4 metal sheets + 4 copper nails + 1 medium leather + 4 sinew + hammer + 1 Tin Solder |

The hammer is used as a tool and is not consumed. Each piece melts back down to nuggets in a forge (see Smelting Recovery). At full durability, the complete copper set fills the Armor stat when worn over the usual leather gloves, shoes, tunic, and trousers.

---

## Wheelbarrow

A wearable cargo container that reduces the effective weight of everything you carry in it.

**Recipe (4 sub-assemblies):**

| Component | Recipe |
|-----------|--------|
| **Wheelbarrow Bucket** | 8 metal sheets + hammer + 1 Tin Solder |
| **Wheelbarrow Handles** | 3 planks + 2 small leather + 2 long sticks + 4 copper nails + sharp knife + hammer |
| **Wheel Rim** | 1 heated metal bar + hammer |
| **Wheel Hub** | Forged: 3 copper nuggets + hammer, or Cast: 2 mud bricks + 2 clay with a Small Molten Crucible (not consumed) |
| **Wheel Assembly** | 1 rim + 1 hub + 1 wood + hammer + 1 Tin Solder |
| **Wheelbarrow** | Bucket + handles + wheel assembly + 1 Tin Solder |

**How to use:** Equip the Wheelbarrow. Items you load into it weigh less than they would in your hands. Permanent — does not degrade from normal use.

**Dismantle:** Returns bucket, handles, and wheel assembly.

**Note:** The Wheelbarrow Bucket is also a required component in the Copper Bathtub blueprint.

---

## Copper Bathtub

A 3-state relaxation and cleansing structure. One of the best morale investments in the mod.

**Recipe:** 1 Wheelbarrow Bucket + 1 Small Copper Stove + 4 planks + 8 mud bricks + 1 large cloth + 4 long sticks + 3 small leather + hammer + 1 Tin Solder.

### The Three States

**Empty Bathtub**
- Place it anywhere.
- Load fuel (firewood, charcoal) into the stove underneath before filling.
- Fill from a water container or directly from a river (drag a filled container onto the tub).

**Full — Cold**
- Take a **cold bath** (moderate cleansing + modest morale boost).
- To heat: Add fuel, then drag a fire source (tinder/torch) to light the stove.

**Full — Warm** (Heat stat: 0–96, drains ~−0.25/daytime point ≈ 1 in-game day of warmth)
- Take a **warm bath**: deep cleansing, major morale boost, spiritual benefit, body warmth.
- Booked as one of the best available morale/hygiene actions in the game.

### Other Actions
- **Add Firewood / Add Fuel** — top up the fuel during heating
- **Empty** — drain the water when done
- **Pick Up** — carry the tub to a new location (works whether it is empty, full or warm; a filled tub is picked up as a filled tub)
- **Dismantle** — recover materials including the wheelbarrow bucket and stove

---

## Metal Lantern (4-Variant Portable Light)

A portable oil-burning light source that exists in four states: item/placed × lit/unlit. Fuel carries across every transform.

### The Four States

| State | Card Type | Carryable | Light output |
|-------|-----------|:---------:|:------------:|
| **Metal Lantern** (item, unlit) | Item | Yes | None — refuel here |
| **Metal Lantern** (item, lit) | Item | Yes | Carried light |
| **Placed Lantern** (unlit) | Placed | No | None — refuel here |
| **Placed Lantern** (lit) | Placed | No | Area light |

**Recipe:** 2 metal sheets + 1 Lantern Oilwell + 1 stone + 1 Tin Solder.

### Fueling the Lantern

Pour **Oil** directly onto the unlit lantern, or drag a **Copper Oil Flask** onto it to add one charge per drag. The lantern holds 3 charges; each charge burns about 6 hours (total ~18 hours per full tank).

### Lighting

Drag any fire source (tinder, torch, campfire) onto an unlit lantern with ≥10% fuel. The lantern ignites.

### When Fuel Runs Out

Lit variants automatically extinguish back to their unlit counterpart. No manual action needed — the lantern just goes dark.

### Placing and Picking Up

- Drag the held lantern item to place it (unlit) as a fixed light source.
- Pick up a placed (unlit) lantern to carry it. If lit, extinguish first.

---

## Oil Chain

Rendered animal fat produces **Oil** — lamp fuel that also provides nutrition in a pinch.

| Blueprint | Recipe | Where |
|-----------|--------|-------|
| **Rendered Oil** | 2 animal fat + 1 clay bowl over a fire | Support tab |
| **Render Fish Oil** | 2 fatty raw fish meat + 1 clay bowl | Support tab |
| **Render Hemp Seed Oil** | 1 hemp seed oil → 1 oil | Support tab (requires H&F installed) |
| **Lantern Oilwell** | 1 metal sheet + 1 twine + hammer + 1 Tin Solder | Metal Tools tab |
| **Copper Oil Flask** | 2 metal sheets + 1 medium leather + hammer + 1 Tin Solder | Metal Tools tab |

**Copper Oil Flask:** Holds 6 oil charges. Drag the flask onto an unlit lantern to pour one charge. A full flask refuels the lantern from empty to full twice over.

**Render Hemp Seed Oil:** Requires the Herbs and Fungi mod to be installed (the hemp seed oil ingredient comes from H&F's oil press chain).

---

## Copper Tea Kettle

A liquid container that heats water on a lit fire or stove.

**Recipe:** 3 metal sheets + hammer (not consumed) + 1 Tin Solder.

**How to use:**
1. Fill the kettle with water (drag a water source or water container onto it).
2. Place the filled kettle on a lit fire or stove. Like a vanilla pot, it fits only a fire with room for its weight: the kettle weighs 180 and its water counts, so a full kettle (about 970) goes on a Fireplace, Fire Pit, Oven or the Copper Stove, and a Campfire (capacity 600) takes it only about half full.
3. The kettle's temperature rises via heat-transfer; the liquid runs its own boil-to-safe-water transform once hot enough.
4. Remove from fire when boiled. Kettle cools down naturally once off the heat source.

**Capacity:** 200-unit Temperature stat; dissipates naturally when removed from the heat source.

---

## Copper Cauldron

A large batch cooking vessel with a weight-limited ingredient basket for cooking many items at once.

**Recipe:** 5 metal sheets + 4 copper nails + hammer (not consumed) + 1 Tin Solder.

**How to use:**
1. Place the cauldron into a lit Fireplace or Fire Pit. It weighs 1200, the same as the vanilla clay cauldron, and follows the same rules: one fits a Fireplace, two fit a Fire Pit, a Campfire is too small, and the Oven refuses it.
2. Load `tag_Cookable` or `tag_Boilable` ingredients into the basket (3000 weight capacity; how many fit depends on their weight).
3. The fire heats the cauldron; recipes run on the contents.
4. Remove cooked items and repeat.

**Liquid brewing:** The cauldron holds 6240 ml of liquid. Fill it with water and add ingredients for batch-brewed soups, stews, or teas.

**Tip:** The Copper Cauldron is far more efficient than individual clay bowls for batch cooking — load it up and let it run while you do other tasks.

---

## Tea Blending Station (3-Variant)

A dedicated herb-processing workbench with six slots, a built-in water reservoir, and a Grind All button.

### The Three Variants

| State | Type | Pickable | Notes |
|-------|------|:--------:|-------|
| **Tea Station Kit** | Item | Yes | Carry and place when ready |
| **Tea Station (unlit)** | Placed | Yes | Drying and grinding; picking it up sets the slot contents down beside you |
| **Tea Station (lit)** | Placed | No | Full function; extinguish before picking up |

**Recipe:** 2 planks + 4 twine + 1 Copper Tea Kettle + 1 Small Copper Stove + 1 rotary quern + 15 heavy stones.

### Using the Six Herb Slots

Place fresh herbs, mushrooms, or dry-able items directly into the six slots:
- Items dry passively over time, even unlit.
- Lighting the stove speeds up drying and unlocks heating/cooking/water-boiling inside the slots.
- Suitable items: anything tagged `tag_Dryable` or `tag_DryableFastSpoilable`.

### Grind All

The "Grind All" action button reads each slot item's own Grind interaction and grinds everything in one step. Works on any dried or millable item — no need to drag grinding tools individually.

### Water Reservoir (built-in, 8 bowls of water)

1. Drag clay bowls of water onto the station to fill the reservoir.
2. **Draw Cold Water:** drag an empty bowl onto the station → fills it from the reservoir.
3. **Draw Boiled Water (Hot):** when the station is lit and the reservoir is at 50%+ heat, drag an empty bowl → fills it with boiled water and consumes one charge.

The lit station heats its own reservoir — cold water in a lit station warms to usable temperature in about 30 in-game minutes.

### Lighting and Extinguishing

- **Light:** Drag a fire source (tinder/torch) onto the unlit station (requires ≥10% fuel).
- **Extinguish:** DismantleAction button.
- **Fuel:** Standard firewood/charcoal; same capacity as the Small Copper Stove.

### Picking Up

The station must be **unlit** before it can be picked up: Pick Up only appears on the unlit station, so extinguish it first. The Kit has no slots, so anything still in the six slots is set down beside you instead of being carried.

---

## Copper Chest

A sealed, insulated storage chest that keeps food fresh far longer than open storage.

**Recipe:** 4 metal sheets + 4 planks + 6 copper nails + hammer + 1 Tin Solder.

**Spoilage reduction:** Items stored inside spoil at **20% of their normal rate** — five times slower than leaving them out.

**Animal-safe:** Wildlife cannot raid the Copper Chest. Unlike the Wooden Pantry, this chest lacks the tag that animals can detect and target.

**Use case:** Long-term preservation of dried herbs, cured meats, or rare ingredients you want to stockpile across seasons.

---

## Smelting Recovery

Most metal items in this mod melt back down to nuggets. Put the item in a heated forge and smelt. Recovery values (from the mod's smelting table):

| Item | Nuggets Recovered |
|------|:-----------------:|
| Copper Nail | 1 |
| Metal Sheet / Wheel Hub / Wheel Rim / Lantern Oilwell / Stove Top Mold / Copper-Rim Chamberpot | 6 |
| Copper Watering Can | 10 |
| Copper Bracers / Helmet / Greaves / Armor | 11 / 15 / 15 / 23 |
| Wheel Assembly | 12 |
| Large Copper Saw | 16 |
| Metal Lantern | 18 |
| Copper Brazier Kit | 22 |
| Copper Chest / Small Copper Stove | 30 |
| Copper Cauldron | 34 |
| Tea Station Kit | 48 |
| Copper Bathtub | 78 |

---

## Tips

- **Build the stove early.** Charcoal output from the copper stove makes heating copper much faster — it generates its own fuel.
- **Equip the pan and wheelbarrow together.** You cook on the go and carry more weight at the same time.
- **The tea station replaces multiple items.** It dries herbs, grinds them, heats water, and brews tea all in one place — worth the resource cost.
- **Oil flasks extend lantern life.** Carry a full flask and you'll never be caught in the dark: its 6 charges refill an empty lantern twice.
- **Bathtub water lasts about a day before it cools.** Heat it just before your rest cycle for maximum morale benefit.
