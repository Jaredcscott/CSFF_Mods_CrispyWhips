# Water Driven Infrastructure — Player Guide

How to build, connect, and operate every machine and structure in the mod.

*(Recipes, Workshop crafts, Ore Sluice odds and fasteners re-checked against the shipped JSON on 2026-10-12, v1.12.4. Other sections last verified 2026-07-28, v1.10.1.)*

---

## Getting Started

Water-powered machines require a river, lake, or stream. Locate one early — your entire infrastructure will be built around it.

**Prerequisites:**
- CSFFModFramework (required)
- Access to a river, lake, or stream for the Mill Race
- **AdvancedCopperTools is optional, not required.** WDI's recipes take the shared Metal Nail, Tin Solder and Metal Sheet that CSFFModFramework 2.28.0 ships, so WDI is fully playable with only the framework installed, and the Workshop's Hammer Copper Sheet / Forge Copper Nails / Hammer Iron Sheet / Forge Iron Nails actions hand over those shared cards. WDI's own rivets, Alloy Solder and cast sheets are retired since 1.12.3 (see Fasteners & Sheets below).

**Suggested build order:**
1. Mill Race → Water Wheel → Water Mill (the foundation)
2. Mill Race Outlet (free water anywhere)
3. Choose one machine first — Sawmill for wood production, Forge for metalworking
4. Expand to Grinding Mill, Ore Sluice, Fishpond once established

**Core research unlock chain:** Mill Race (16t) → Water Wheel (48t) → Water Mill (48t) → everything downstream.

---

## Step 1: Build a Mill Race

The Mill Race is a wooden channel that brings river water to your machines. It must be your first build.

**Blueprint:** Advanced Tools tab → Mill Race (unlock 16 ticks research)
**Requires:** 6 Planks

**Placement:** Must be adjacent to a water source (river, lake, or stream). After placing, you'll see directional mill race improvement options (North/South/East/West) in the **Environment Improvements** panel of nearby outdoor locations — use these to route the channel toward your build site.

**Dismantle:** Pack Up returns to kit form.

---

## Step 2: Build a Water Wheel & Water Mill

Once your Mill Race is routed, build the power source.

**Water Wheel**
- Blueprint: Advanced Tools tab (unlock 48 ticks)
- Requires: 25 Planks + 20 Stone + 5 Heavy Stone + 1 Large Copper Gear
- Place adjacent to the Mill Race
- The Water Wheel alone doesn't do anything — it feeds into the Water Mill

**Water Mill**
- Blueprint: Advanced Tools tab (unlock 48 ticks)
- Requires: 1 Water Wheel + 1 Mill Race + 1 Large Copper Gear
- This is the base structure all downstream machines connect to

**Copper Gears** are cast in a forge or crucible pipeline (Metal Crafts tab):
- **Cast Large Copper Gear**: large molten copper crucible + mold + hammer (unlock 16 ticks)
- **Cast Small Copper Gear**: small molten copper crucible + mold + hammer (unlock 8 ticks)
- **Cast Large / Small Bronze Gear** (only with Advanced Copper Tools installed): a large or small molten tin, tin bronze or white bronze crucible + mold + hammer (unlock 16 / 8 ticks). A Bronze Gear is accepted in place of a Copper Gear of the same size in WDI recipes. No stat bonus.
- **Forge Iron Parts / Bearing / Axle / Wrench**: 1 heated iron bar each (unlock 8 ticks each). Bearing, Axle and Wrench also need a hammering tool, which you keep; Iron Parts needs no tool and makes 2.

---

## Mill Race Outlet (Free Water Anywhere)

The Mill Race Outlet taps your Mill Race to provide unlimited unclean water at any outdoor location — no more hauling water from the river.

**Blueprint:** Advanced Tools tab → Mill Race Outlet (unlock 32 ticks)
**Requires:** 1 Mill Race + 4 Planks + 4 Metal Nails (dismantling the kit returns all of it, the nails as copper Metal Nails)

**How to use:**
1. Build one Mill Race and connect it to your water source.
2. Craft and place a Mill Race Outlet Kit at any outdoor location you want water access.
3. Use the **Draw Unclean Water** action — drag any water container (clay bowl, copper kettle, flask, etc.) onto the outlet to fill it.
4. Water drawn is unclean — purify before drinking (boil it, or use the wearable metal pan on a fire).

**Seasonal behavior:** The outlet **freezes in winter** — no water can be drawn until spring thaw. Plan ahead and stockpile clean water before winter arrives.

**Winter heat:** a frozen outlet still lets the stations in its location work while heat stands there: a lit Campfire, Fire Pit, Fireplace, Oven, Kiln, Furnace, Bloomery or Forge, a lit Copper Brazier (Advanced Copper Tools), or a Water-Driven Forge or Workshop at least half hot. Heat does not thaw the outlet, so a Rain Cistern in the same location stays unfed until spring.

**Chip Ice (winter):** While frozen, the outlet offers a **Chip Ice** action (2 daytime) that yields an **Ice Block** — stock an Ice Pit for summer food storage, or melt it for water.

**Pack Up:** returns the outlet to kit form and moves it elsewhere.

---

## Water-Driven Sawmill

Automated wood processing — drag logs in, collect planks.

### Building

Multi-stage build (unlock 96 ticks, requires Water Mill):

| Stage | Materials |
|-------|-----------|
| 1 | 25 Heavy Stone + 10 Plaster + Wooden Shovel (keep) + Metal Shovel (keep) |
| 2 | 8 Rope |
| 3 | Forge Hammer (keep) + 20 Planks |
| 4 | Water Mill + Mill Race + 2× Large Copper Gear + 4× Small Copper Gear + Copper Saw Blade + 2× Iron Parts + 2× Iron Bearings + 1× Iron Axle + 12× Metal Nails* + 1 Tin Solder* |

The **Copper Saw Blade** (Metal Crafts tab): one large molten copper crucible + saw-blade mold + hammer (unlock 16 ticks).

### Using the Sawmill

1. Place the sawmill near your Mill Race network.
2. Drag a **log** onto the sawmill to trigger the **Cut** action (30 min).
3. The log is consumed and **8 Planks + 2 Wood Shavings** are produced directly onto the ground/board. The shavings make excellent tinder. The planks keep the
   log's wood and dryness (a birch log gives birch planks).
4. The sawmill has a **6-slot inventory** that takes logs (capacity 20,000 weight; a log weighs 3,000), so you can pre-load it for batch processing.
5. Press **Saw All** (1 hour) and every log inside is sawn at once: 8 Planks + 2 Wood Shavings per log, each log's planks keeping that log's own wood and dryness. The saw blade and anything else inside stays, and Saw All trains no skill. To load logs, open the sawmill's inventory and drag them into its slots (dragging a log onto the sawmill itself runs **Cut**).

### Pack Up

Use the **Pack Up** dismantle action (1 hour) to return the sawmill to a portable kit.

---

## Water-Driven Forge

A high-temperature water-powered forge for smelting and batch metalworking, plus a water-hammer.

### Building

Multi-stage build (unlock 64 ticks, requires Water Mill):

| Stage | Materials |
|-------|-----------|
| 1 | Water Mill + Wooden Shovel (keep) + Metal Shovel (keep) |
| 2 | 40 Stone + 40 Mud Brick + 10 Planks |
| 3 | 10 Clay + 1 Leather Bellows + 10 Planks |
| 4 | 20 Planks + 10 Mud Brick + 10 Plaster + 2× Iron Parts + 2× Iron Bearings + 1× Iron Axle + 10× Metal Nails* + 1 Tin Solder* |

### Forge Stats

| Stat | Details |
|------|---------|
| **Temperature** | 0–1800°. Natural heat gain slows the hotter it gets and stops entirely at 1000° — you need Blast or Bellows to push past that. Above ~1000°, it **loses heat fast (−300/tick ≈ −1200°/hour) without windflow**; below 1000° with no fuel at all it still bleeds at roughly −400°/hour. |
| **Fuel** | 0–96 units; consumed by feeding/actions, not passively |
| **Windflow (Bellows)** | 0–8; a windy environment grants +8 windflow and +120°/hour naturally — otherwise Blast is your source of heat above 1000° |

### Getting the Forge Hot

1. **Add Fuel:** Drag firewood (+20 fuel), charcoal (+25), or embers (+16) onto the forge.
2. **Light:** Drag tinder or a torch onto the forge (requires at least some fuel) → +400°.
3. **Blast:** Use the Blast DismantleAction (1 hour) → +480° temperature, costs 16 fuel. Use this to reach smelting temperature quickly. Each Blast leaves a pile of **Ash** behind (lye and soil uses). With Advanced Copper Tools installed, Blast also melts the ACT metal items you have loaded into the forge or workshop (sheets, armour, shields, saws, pans and more) into Metal Nuggets of each item's own metal: Iron 200, Tin Bronze 130, Ghost Bronze 110, Tin 120, White Bronze 140, copper 100.

**To reach smelting temperature (1100°+):** Light the forge (+400°) then Blast twice (+480° each) → 1360°, well past the threshold. In a windy environment, natural heat gain alone can also carry you there over time.

### Smelting

- **Smelt Ore** (CardInteraction — drag copper ore): requires the forge at smelting temperature (1100°+). Produces 1–2 Copper Ingots; costs −400° temperature and −8 fuel.
- **Smelt Greenstone** (auto CookingRecipe at 1100°+): Greenstone Powder inside the forge smelts into one copper Metal Nugget at the powder's Quality, plus one Greenstone Slag, once the forge is hot enough.
- **Smelt High-Heat Ore / Iron Components** (Parts/Bearing/Axle/Wrench, `tag_SmeltsAt1100`): same 1100°+ threshold as copper. A Bearing, Axle or Wrench smelts back into 6 iron-typed metal nuggets and an Iron Parts into 3 (not a bar), at the Metal Quality of the bar it was forged from. Parts made before 1.12.3 melt at quality 0.

### Pack Up

**Pack Up** dismantle action (5 daytime) returns the forge to kit form.

---

## Water-Driven Workshop (Forge Upgrade)

The Workshop is an upgraded Forge with 14 inventory slots and water-hammer batch actions for metalworking.

### Building

Blueprint: Furniture tab → Water-Driven Workshop Kit (unlock 32 ticks)
**Requires:** Existing Forge Kit + Water Mill + Forge Hammer + 10 Charcoal + 2 Iron Parts + 2 Iron Bearings + 1 Iron Axle + 1 Iron Wrench + 2 Small Copper Gears + 1 Large Copper Gear + 8 Metal Nails* + 1 Tin Solder*

### Workshop-Exclusive Actions

All batch actions require the forge at smelting temperature (1100°+, same gate as the Forge's Smelt Ore) and fuel ≥1%:

| Action | Time | Input | Output |
|--------|------|-------|--------|
| **Hammer All** | 30 min | Items in inventory | Applies one water-hammer strike to each item simultaneously |
| **Hammer Copper Sheet** | 1 hour | 6 copper nuggets (from inventory) | 1 Copper Sheet (the shared Metal Sheet) at the average Metal Quality of the six nuggets |
| **Forge Copper Nails** | 1 hour | 6 copper nuggets (from inventory) | 6 Copper Nails (the shared Metal Nail) |
| **Forge Iron Nails** | 1 hour | 6 iron-grade nuggets (Metal Type Iron, from inventory) | 6 Iron Nails (the shared Metal Nail at Metal Type Iron) |
| **Hammer Iron Sheet** | 1 hour | 6 iron-grade nuggets (from inventory) | 1 Iron Sheet (the shared Metal Sheet at Metal Type Iron) at the average Metal Quality of the six nuggets |
| **Cast Metal Lump** | 1 hour | 6 copper nuggets (from inventory) | 1 Unfinished Metal Bar at the average quality of the six nuggets, ready for Hammer All |

Each craft takes only nuggets of its own grade: copper nuggets never feed an iron craft, and iron nuggets (the Ore Sluice makes them, and so does smelting an iron part) never feed a copper one. The Workshop's heat gates the iron crafts the same way it gates the copper ones.

### What to Load

The Workshop accepts 14 inventory slots and takes tool blanks, copper/iron components, clay, and smeltable metal parts (`tag_SmeltsAt1100`).

**Hammer All workflow:**
1. Load all your in-progress metal items (tool blanks, gears, etc.) into the workshop inventory.
2. Heat the forge to smelting temperature.
3. Press **Hammer All** — all items are struck in one 30-minute action.
4. Repeat as needed to fully work each item.

This replaces individually dragging a hammer tool onto each item.

---

## Water-Driven Grinding Mill

The water wheel drives the millstone — automates all grinding tasks.

### Building

Blueprint: Furniture tab (unlock 96 ticks, requires Water Mill)
**Requires:** Water Mill + Rotary Quern + 20 Planks + 10 Stone + 1 Iron Bearing + 1 Iron Axle + 8 Metal Nails* + 1 Tin Solder*

### Using the Grinding Mill

1. Load grindable items into the mill's inventory.
2. Use the **Grind All** action — all contents are ground in one batch.

The mill accepts every vanilla grindable: wheat and rye grains (→ flour), edible acorns (→ acorn flour), bone splinters (→ bonemeal), charcoal (→ ash), and more — anything a grinding tool can process. No grinding tool needed.

**Pack Up** returns the mill to kit form.

---

## Ore Sluice

Uses flowing water to wash soil and separate mineral deposits — a probabilistic panning-style machine.

### Building

Two-stage build (unlock 16 ticks, requires Mill Race):

| Stage | Materials |
|-------|-----------|
| 1 — Sluice Frame | 12 Planks + 6 Metal Nails* + hammering tool |
| 2 — Ore Sluice | 10 Planks + 4 Heavy Stone + 1 Sluice Frame + 6 Metal Nails* |

Placement must be adjacent to a Mill Race.

### Using the Ore Sluice

1. Load **Mud Piles**, **Dirt Piles**, or **Fine Dirt** — the only accepted inputs (up to 12 slots, 5000 weight).
2. Use the **Sluice All** action (45 min) — each loaded item is rolled independently for a result.
3. Every roll can produce a nugget, ore, stone, flint or clay, and outcomes are not mutually exclusive. Per-item drop chance by soil type:

| Result | Mud Pile | Dirt Pile | Fine Dirt |
|--------|:--------:|:---------:|:---------:|
| Copper Nugget | 2% | 1% | 0.5% |
| Iron Nugget | 1% | 0.5% | 0.25% |
| Tin Nugget | 1% | 0.5% | 0.25% |
| Greenstone (Copper Ore) | 3% | 2% | 1% |
| Flint | 30% | 20% | 10% |
| Stone | 40% | 20% | 10% |
| Clay | 10% | 20% | 55% |

Nuggets spawn at Metal Quality 35. A pile whose rolls all miss leaves a Stone, so a wash is never empty. The metal odds were cut in 1.12.3 (mud copper was 8%, greenstone 10%), so the sluice supplements the game's copper mine rather than replacing it.

**Tip:** Mud gives the best odds at metal nuggets and Greenstone; Fine Dirt is heavily weighted toward Clay. Stockpile before running the sluice — each Sluice All processes the whole loaded batch at once.

**Pack Up** returns the sluice to kit form.

---

## Fishpond

A dug pond that breeds fish passively. Harvesting most species requires a spear nearby; crayfish and (once stocked) frogs are caught by hand.

### Building

Multi-stage build (unlock 64 ticks, requires Mill Race):

| Stage | Materials |
|-------|-----------|
| 1 | Dig with shovel (−25 durability) |
| 2 | Dig with shovel (−25 durability) |
| 3 | Dig with shovel (−25 durability) |
| 4 — Line | 10 Planks + 4 Metal Nails* + 3 Bugs + 15 Heavy Stone + 30 Stone |
| 5 — Supplement | 2 Pike + 2 Perch + 2 Minnow |

### Fish Species

| Species | Breeds naturally? | Catch tool | Catch time |
|---------|:-----------------:|-----------|:----------:|
| **Pike** | Yes (~0.5%/DTP) | Spear | 2 daytime |
| **Perch** | Yes (~0.5%/DTP) | Spear | 2 daytime |
| **Minnow** | Yes (~0.5%/DTP) | Spear | 2 daytime |
| **Trout** | No (stock only, cap 10) | Spear | 2 daytime |
| **Char** | No (stock only, cap 10) | Spear | 2 daytime |
| **Sturgeon** | No (stock only, cap 10) | Spear | 2 daytime |
| **Eel** | No (stock only, cap 10) | Spear | 2 daytime |
| **Crayfish** | No (stock only, cap 10) | None (hand) | 1 daytime |
| **Frog** | Once the pond promotes to Stocked (breeding population >10) | None (hand) | 1 daytime |

**Breeding:** Only Pike, Perch, and Minnow breed, each at roughly 0.5%/daytime point toward a cap of 10. Once their combined population exceeds 10, the pond promotes from Filled to Stocked and Frog catching unlocks. Trout, Char, Sturgeon, and Eel are tracked separately (cap 10 each) and never breed — you must stock them directly.

**Stocking:** Drag a Live [Species] card directly onto the placed fishpond to add it.

### Winter

The fishpond **freezes in winter** — open-water catch actions disappear, replaced by **Ice Fishing** variants. All fish populations are preserved through winter. The pond thaws automatically in spring.

### Drawing Water

Drag any water container onto the fishpond to fill it with unclean water from the pond. Purify before drinking.

### Pack Up

The **Drain** action abandons all fish (this permanently discards the population) and returns the pond to kit form. Use only when relocating.

---

## Fasteners & Sheets

Every WDI blueprint slot that calls for a nail takes the framework's shared **Metal Nail** of any metal, and every solder slot its **Tin Solder** (Forge Tin Solder: one tin nugget makes 2; the Ore Sluice washes tin nuggets out). Sheet slots read the sheet's metal: the Water-Driven Stamp Mill takes 2 **Iron Sheets** (Metal Sheets at Metal Type Iron) and the Water Basin 1 Metal Sheet of any metal but iron. Expand on the Sawmill or Workshop costs 4 **Iron Nails**; copper or bronze nails do not count.

`*` in the tables above marks a slot that takes the shared Metal Nail or Tin Solder.

**Retired parts (1.12.3).** WDI's own Cast Copper Rivet, Cast Iron Rivet, Alloy Solder, Cast Copper Sheet and Cast Iron Sheet, and their six blueprints (Cut Iron Rivets from Sheet included), are gone from the journal: they duplicated the shared cards under near-identical names and the cast sheets could not be melted back. A card already in your save is now named "(old)" and becomes the shared card of the same metal by itself within a quarter of an hour, or at once with its Reforge button.

---

## Infrastructure Planning Tips

**Power chain integrity:** The Mill Race → Water Wheel → Water Mill chain must be complete before any downstream machine works. If a machine stops functioning, check that the Mill Race is connected and the Water Mill is placed correctly.

**Build the Forge before the Sawmill.** The Sawmill requires cast copper gears and a copper saw blade — you need the Forge to produce them. Build forge first, cast your gears, then build the sawmill.

**Mill Race Outlets are cheap.** Once you have one Mill Race, build outlets at every camp location you frequent. Unclean water access everywhere eliminates most water-hauling trips.

**The Workshop is worth the upgrade cost.** Hammer All on 14 items in 30 minutes vs. striking each one individually is a massive time saving for any metalworking-heavy playthrough.

**The Ore Sluice rewards a big stockpile, and material choice matters.** Mud gives the best shot at metal nuggets and Greenstone; Fine Dirt mostly returns Clay. Run it in large batches rather than a few items at a time.

**Winter planning:** Outlets and fishponds freeze. Build up a clean water reserve before the first winter and pre-catch fish you want before freeze.
