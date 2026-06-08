# Water Driven Infrastructure — Player Guide

How to build, connect, and operate every machine and structure in the mod.

---

## Getting Started

Water-powered machines require a river or lake. Locate one early — your entire infrastructure will be built around it.

**Prerequisites:**
- Advanced Copper Tools installed (provides copper ingots, metal sheets, and nails)
- Access to a river, lake, or stream for the Mill Race

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

**Dismantle:** Returns 6 Planks.

---

## Step 2: Build a Water Wheel & Water Mill

Once your Mill Race is routed, build the power source.

**Water Wheel**
- Blueprint: Advanced Tools tab (unlock 48 ticks)
- Requires: 25 Planks + 20 Stone + 5 Clay + 1 Large Copper Gear
- Place adjacent to the Mill Race
- The Water Wheel alone doesn't do anything — it feeds into the Water Mill

**Water Mill**
- Blueprint: Advanced Tools tab (unlock 48 ticks)
- Requires: 1 Water Wheel + 1 Mill Race + 1 Large Copper Gear
- This is the base structure all downstream machines connect to

**Copper Gears** are cast in a forge or foundry:
- **Cast Large Copper Gear** (Metal Crafts tab): Large Molten Crucible + Clay + Stone + hammer (unlock 16 ticks)
- **Cast Small Copper Gear** (Metal Crafts tab): Small Molten Crucible + Clay + Stone + hammer (unlock 8 ticks)
- **Forge Iron Parts** (Metal Crafts tab): Wrought Iron Bar + hammer (unlock 8 ticks)
- **Forge Iron Bearing** (Metal Crafts tab): Wrought Iron Bar + hammer (unlock 8 ticks)
- **Forge Iron Axle** (Metal Crafts tab): Wrought Iron Bar + hammer (unlock 8 ticks)
- **Forge Iron Wrench** (Metal Crafts tab): Wrought Iron Bar + hammer (unlock 8 ticks)

---

## Mill Race Outlet (Free Water Anywhere)

The Mill Race Outlet taps your Mill Race to provide unlimited unclean water at any outdoor location — no more hauling water from the river.

**Blueprint:** Advanced Tools tab → Mill Race Outlet (unlock 32 ticks)
**Requires:** 1 Mill Race + 4 Planks

**How to use:**
1. Build one Mill Race and connect it to your water source.
2. Craft and place a Mill Race Outlet Kit at any outdoor location you want water access.
3. Use the **Draw Unclean Water** action — drag any water container (clay bowl, copper kettle, flask, etc.) onto the outlet to fill it.
4. Water drawn is unclean — purify before drinking (boil it, or use the wearable metal pan on a fire).

**Seasonal behavior:** The outlet **freezes in winter** — no water can be drawn until spring thaw. Plan ahead and stockpile clean water before winter arrives.

**Pack Up:** 2-daytime action to return to kit form and move elsewhere.

---

## Water-Driven Sawmill

Converts logs into planks automatically — 1 log → 8 planks per action.

### Building

Multi-stage build (unlock 96 ticks, requires Water Mill):

| Stage | Materials |
|-------|-----------|
| 1 | Clay + Iron + Bellows + Flume |
| 2 | Stone |
| 3 | Saw + Planks |
| 4 | Water Mill + Mill Race + 2× Large Gear + 4× Small Gear + Saw Blade |

The **Copper Saw Blade** (Metal Crafts tab) is a cast component: Large Molten Crucible + 10 Clay + 10 Stone + hammer.

### Using the Sawmill

1. Place the sawmill near your Mill Race network.
2. Drag a **log** onto the sawmill to trigger the **Cut** action (2 daytime cost).
3. The log is consumed and **8 Planks** are produced directly onto the ground/board.
4. The sawmill has a **6-slot inventory** — pre-load it with logs for batch processing.

### Pack Up

Use the **Pack Up** dismantle action (4 daytime) to return the sawmill to a portable kit. Logs loaded in inventory are preserved.

---

## Water-Driven Forge

A high-temperature forge powered by a water wheel. Smelts standard copper at lower temperatures and Greenstone at 1100°+. Also used for casting copper components.

### Building

Multi-stage build (unlock 64 ticks, requires Water Mill):

| Stage | Materials |
|-------|-----------|
| 1 | Water Mill + Bellows + Flume |
| 2 | 40 Stone + 40 Clay + 10 Planks |
| 3 | 10 Brick + 1 Grate + 10 Planks |
| 4 | 20 Planks + 10 Clay + 10 Iron |

### Forge Stats

| Stat | Details |
|------|---------|
| **Temperature** | 0–1300°; cools −40°/daytime when idle |
| **Fuel** | 0–96 units; consumed by actions, not passively |
| **Windflow (Bellows)** | 0–8; decays −4/daytime after a Blast |

### Getting the Forge Hot

1. **Add Fuel:** Drag firewood (+20 fuel), charcoal (+25), or embers (+16) onto the forge.
2. **Light:** Drag tinder or a torch onto the forge (requires at least some fuel) → +400°.
3. **Blast:** Use the Blast DismantleAction (4 daytime) → +480° temperature, costs 16 fuel. Use this to reach smelting temperature quickly.

**To reach Greenstone smelting (1100°+):** Light the forge (+400°), then Blast once (+480°) → 880°. Blast again → 1360° (capped at 1300°). Two blasts gets you there.

### Smelting

- **Smelt Ore** (CardInteraction — drag copper ore): requires ≥780° (60% of max). Produces 1–2 Copper Ingots; costs −400° temperature and −8 fuel.
- **Smelt Greenstone** (auto CookingRecipe at 1100°+): Greenstone inside the forge automatically converts to Copper Ingots when the forge is hot enough.

### Pack Up

**Pack Up** dismantle action (5 daytime) returns the forge to kit form.

---

## Water-Driven Workshop (Forge Upgrade)

The Workshop is an upgraded version of the Forge with 14 inventory slots and water-hammer batch actions for metalworking.

### Building

Blueprint: Furniture tab → Water-Driven Workshop Kit (unlock 32 ticks)
**Requires:** Existing Forge Kit + Water Mill + Bellows + Charcoal + 2 Iron Parts + 2 Iron Bearings + 1 Iron Wrench + 2 Small Gears + 1 Large Gear

### Workshop-Exclusive Actions

All actions require temperature ≥780° and fuel ≥1:

| Action | Time | Input | Output |
|--------|------|-------|--------|
| **Hammer All** | 2 daytime | Items in inventory | Applies one water-hammer strike to each item simultaneously |
| **Hammer Copper Sheet** | 4 daytime | 6 copper nuggets (from inventory) | 1 Metal Sheet |
| **Forge Copper Nails** | 4 daytime | 6 copper nuggets (from inventory) | 6 Copper Nails |

### What to Load

The Workshop accepts 14 inventory slots and takes tool blanks, copper components (small and large), clay, and smeltable copper parts (`tag_SmeltsAt1100`).

**Hammer All workflow:**
1. Load all your in-progress metal items (tool blanks, gears, etc.) into the workshop inventory.
2. Heat the forge to ≥780°.
3. Press **Hammer All** — all items are struck in one 2-daytime action.
4. Repeat as needed to fully work each item.

This replaces individually dragging a hammer tool onto each item.

---

## Water-Driven Grinding Mill

Automated batch grinding — load millable materials, press Grind All.

### Building

Blueprint: Furniture tab (unlock 96 ticks, requires Water Mill)
**Requires:** Water Mill + Grinding Stone + 20 Planks + 10 Stone

### Using the Grinding Mill

1. Load up to **18 slots** of `tag_Millable` items into the mill's inventory.
2. Use the **Grind All** action (2 daytime) — all contents are ground in one batch.

The mill accepts grain, dried herbs, mushrooms, and any other millable material. No grinding tool needed.

**Pack Up** (4 daytime) returns the mill to kit form.

---

## Ore Sluice

Washes soil to recover minerals. A probabilistic gold-panning style machine.

### Building

Two-stage build (unlock 16 ticks, requires Mill Race):

| Stage | Materials |
|-------|-----------|
| Sluice Frame | 12 Planks + 6 Copper Nails + hammer (−15 durability) |
| Ore Sluice | 10 Planks + 4 Stone + 1 Sluice Frame |

Place adjacent to a Mill Race.

### Using the Ore Sluice

1. Collect **Mud Piles**, **Dirt Piles**, or **Fine Dirt** — these are the only accepted inputs (up to 12 slots, 5000 weight).
2. Use the **Sluice All** action (3 daytime) — the sluice washes all loaded soil.
3. Each soil item produces one result:

| Output | Probability |
|--------|:-----------:|
| **Stone** | 85% |
| **Flint** | 10% |
| **Greenstone (Copper Ore)** | 5% |

**Tip:** The sluice is most efficient when you have a large stockpile of mud or dirt. Each Sluice All processes all 12 slots simultaneously — fill the inventory completely before running it.

**Pack Up** (3 daytime) returns the sluice to kit form.

---

## Fishpond

A dug pond that breeds fish passively. Harvesting requires a spear for larger fish; crayfish can be caught by hand.

### Building

Multi-stage build (unlock 64 ticks, requires Mill Race):

| Stage | Action |
|-------|--------|
| 1 | Dig with shovel (−25 durability) |
| 2 | Dig with shovel (−25 durability) |
| 3 | Dig with shovel (−25 durability) |
| 4 | Stock: 10 Pike + 3 Perch + 15 Minnow + 30 Planks |
| 5 | Supplement: 2 Pike + 2 Perch + 2 Minnow |

### Fish Species

| Species | Breeds naturally? | Catch tool | Catch time |
|---------|:-----------------:|-----------|:----------:|
| **Pike** | Yes | Spear | 2 daytime |
| **Perch** | Yes | Spear | 2 daytime |
| **Minnow** | Yes | Spear | 2 daytime |
| **Trout** | No (stock only) | Spear | 2 daytime |
| **Char** | No (stock only) | Spear | 2 daytime |
| **Sturgeon** | No (stock only) | Spear | 2 daytime |
| **Crayfish** | No (stock only) | None (hand) | 1 daytime |
| **Frog** | Once stocked (≥10 fish total) | None (hand) | 1 daytime |

**Breeding:** Pike, Perch, and Minnow grow at ~0.5%/daytime point naturally. A fully established pond from the founding stock takes about 200 daytime points to reach maximum population on each breeding species.

**Stocking:** Drag a Live [Species] card directly onto the placed fishpond to add it.

**Frog:** Once the total fish population exceeds 10, a Frog catch action becomes available.

### Winter

The fishpond **freezes in winter** — open-water catch actions disappear, replaced by **Ice Fishing** variants (+1 daytime cost each). All fish populations are preserved through winter. The pond thaws automatically in spring.

### Drawing Water

Drag any water container onto the fishpond to fill it with unclean water from the pond. Purify before drinking.

### Pack Up

The **Drain** action (2 daytime) abandons all fish and returns the pond to kit form. Use only when relocating.

---

## Infrastructure Planning Tips

**Power chain integrity:** The Mill Race → Water Wheel → Water Mill chain must be complete before any downstream machine works. If a machine stops functioning, check that the Mill Race is connected and the Water Mill is placed correctly.

**Build the Forge before the Sawmill.** The Sawmill requires cast copper gears and a copper saw blade — you need the Forge to produce them. Build forge first, cast your gears, then build the sawmill.

**Mill Race Outlets are cheap.** Once you have one Mill Race, build outlets at every camp location you frequent. Unclean water access everywhere eliminates most water-hauling trips.

**The Workshop is worth the upgrade cost.** Hammer All on 14 items in 2 daytime vs. striking each one individually is a massive time saving for any metalworking-heavy playthrough.

**The Ore Sluice pays off slowly.** At 5% Greenstone per soil unit, expect to run the sluice dozens of times before seeing meaningful copper ore yields. Best used alongside a large dirt/mud stockpile from other digging activities.

**Winter planning:** Outlets and fishponds freeze. Build up a clean water reserve before the first winter and pre-catch fish you want before freeze.
