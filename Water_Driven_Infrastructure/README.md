# Water Driven Infrastructure

**Version:** 1.12.12
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.69c)
**Requires:** CSFFModFramework 2.28.0 or later. Advanced Copper Tools is optional and adds extras, but nothing needs it.

---

## Overview

Build a water wheel on a river, then run sawmills, forges, grinding, stamp and fulling mills, ore sluices and fishponds from it. A Mill Race Outlet brings unclean water to any outdoor location, so you stop walking to the river.

The mod stands alone with the framework. Its nails, solder and sheets are the framework's shared Metal Nail, Tin Solder and Metal Sheet, so ones made with any mod fit WDI recipes.

Since 1.12.0 a running outlet also fills a Rain Cistern for winter, and you can feed your fishpond, plant reed and cattail beds, and build a Water Basin that raises Sanctuary. Working the stations trains your skills.

26 of the 28 blueprints are in the journal from the start. Cast Large and Small Bronze Gear appear only with Advanced Copper Tools.

The mod ships 29 blueprints, 26 items, 21 placed structures (each form of a card counts), 4 Mill Race improvements, 7 perks, 12 Partner duties and 1 setting. It also keeps five retired parts and their six blueprints so old saves load (see **Retired parts**).

---

## Build chain

```
Water Source (river / lake)
 └── Mill Race ──────────────────────→ Mill Race Outlet  (draw water anywhere outdoors; fills a Rain Cistern)
                   ├── Reed Bed / Cattail Bed
                   ├── Water Basin
                   ├── Retting Pit
                   ├── Irrigation Channel
                   └── Water Wheel
                          └── Water-Driven Mill
                                 ├── Water-Driven Sawmill
                                 ├── Water-Driven Forge ──→ Water-Driven Workshop (upgrade)
                                 ├── Water-Driven Grinding Mill
                                 ├── Water-Driven Stamp Mill
                                 ├── Water-Driven Fulling Mill
                                 ├── Ore Sluice
                                 └── Fishpond
```

### Blueprint tabs

| Tab | Blueprints |
|-----|-----------|
| **Advanced Tools** (7) | Mill Race, Mill Race Outlet, Water Wheel, Overshot Wheel, Water-Driven Mill, Ore Sluice Frame, Fish Funnel |
| **Metal Crafts** (7, +2 with ACT) | Cast Large Copper Gear, Cast Small Copper Gear, Cast Copper Saw Blade, Forge Iron Parts, Bearing, Axle and Wrench. With Advanced Copper Tools, also Cast Large and Small Bronze Gear. |
| **Furniture** | Ore Sluice, Water-Driven Grinding Mill, Fulling Mill, Sawmill, Forge, Workshop Kit, Stamp Mill, Water Basin, Retting Pit |
| **Farming Agriculture** | Fishpond, Reed Bed, Cattail Bed, Irrigation Channel |

The four Mill Race improvements appear in the **Environment Improvements** panel of each outdoor location, not in the journal. Locations that other mods add to the world map join the water network automatically.

---

## Mill Race and Outlet

**Mill Race:** a wooden channel that carries river or lake water to your machines. Most other blueprints need one. Build it next to water. Research takes 4 hours and the build is one 1-hour stage.

Finishing a Mill Race also connects the location at its far end, even one you have never visited. A race still under construction connects nothing.

**Mill Race Outlet:** draw unclean water at any outdoor location. Build a Mill Race first, then the outlet (research 24 hours, two 1-hour stages: a footing of 12 Stone and 4 Heavy Stone, then a Mill Race, 4 Wooden Planks and 4 Metal Nails*). The water never runs out, but purify it before drinking. It freezes in winter and thaws in spring.

- **Control Valve:** "Close Valve" (15 minutes) shuts the outlet. Nothing can be drawn, a Rain Cistern there stops filling, and stations that rely on it stop. "Open Valve" (15 minutes) reverses it. A closed outlet neither freezes nor thaws.
- **Winter heat:** stations at a frozen outlet keep working if a heat source stands there. That means a lit Campfire, Fire Pit, Fireplace, Oven, Kiln, Furnace, Bloomery, Forge or Copper Brazier, or a Water-Driven Forge or Workshop at Temperature 900 or more. Heat does not thaw the outlet.
- **Rain Cistern feed:** a Rain Cistern at the same location fills with River Water at 10 every 15 minutes, about 22 days from empty, so water banked in summer and autumn lasts into winter. Each outlet adds its own flow.
- A cistern already holding rainwater or any other liquid is left alone until drawn empty. A frozen outlet feeds nothing until the thaw.

---

## Water Wheel, Mill and Overshot Wheel

**Water Wheel:** the main power source. Build it next to a Mill Race. Research 12 hours. Cost: 25 Wooden Planks, 20 Stone, 5 Heavy Stone and 1 Large Copper Gear (1.5 hours). Its card art is animated.

**Water-Driven Mill:** turns the wheel's rotation into power, and every downstream machine builds on it. Research 12 hours. Cost: 1 Water Wheel, 1 Mill Race and 1 Large Copper Gear (3 hours).

**Overshot Wheel:** a faster wheel you fit to a finished station. Research 12 hours. Cost: 1 Water Wheel, 1 Iron Axle, 1 Large Copper Gear and 1 Iron Bearing (1.5 hours).

Drag it onto a Sawmill, Grinding Mill, Workshop or Ore Sluice (30 minutes). That station's batch button is faster when you press it yourself:

| Button | Normal | With wheel |
|--------|--------|-----------|
| Saw All | 3 hours | 1.5 hours |
| Grind All, Hammer All | 30 minutes | 15 minutes |
| Sluice All | 45 minutes | 15 minutes |

A Partner's duty work takes no time either way, so the wheel cannot speed a Partner up. A fitted station shows **Pack Up (Overshot)**, which also returns the wheel. The Forge and Stamp Mill take no wheel. Times may be rebalanced.

---

## Water-Driven Sawmill

Drag a log onto the sawmill to cut planks.

**Build** (research 24 hours, four 3-hour stages, needs a Water-Driven Mill):

| Stage | Materials |
|-------|-----------|
| 1 | 25 Heavy Stone, 10 Plaster, a shovel (kept) |
| 2 | 8 Rope |
| 3 | 20 Wooden Planks, a Forge Hammer or Stone (kept) |
| 4 | Water-Driven Mill, Mill Race, 2 Large Copper Gears, 4 Small Copper Gears, Copper Saw Blade, 2 Iron Parts, 2 Iron Bearings, 1 Iron Axle, 12 Metal Nails*, 1 Tin Solder* |

- **Cut** (1.5 hours): a log gives 8 Wooden Planks and 2 Wood Shavings. The planks keep the log's wood and dryness, so a birch log gives birch planks.
- **Saw All** (3 hours): the sawmill holds 20,000 weight (6 logs). Saw All saws every log inside, each giving what Cut gives. It trains no skill.
- **Saw Blade:** every log sawn, by Cut or Saw All, wears the blade by 1. After 60 logs Cut and Saw All stop, and Saw All saws only the logs the blade has left. Drag a Copper Saw Blade onto the sawmill and **Replace Saw Blade** (30 minutes) fits it, using the blade up. Packing up keeps the wear.
- **Expand** (30 minutes): rebuilds the sawmill in place as the **Expanded Water-Driven Sawmill**, holding 30,000 instead of 20,000 (10 logs). Contents and a fitted Overshot Wheel stay. It costs 6 Wooden Planks and 4 Iron Nails (Metal Nails at Metal Type Iron), taken from your inventory or cards at the location.
- There is no way back from Expand, and Pack Up returns the plain kit. Cost and capacity may change.
- **Pack Up** (1 hour) returns the kit.
- **Operate the Sawmill** (Partner duty): mark it and a Partner cuts logs there. The expanded sawmill has its own duty.

---

## Water-Driven Forge and Workshop

A water-powered forge that smelts at 1,100 degrees and above.

**Build** (research 48 hours, four 3-hour stages, needs a Water-Driven Mill):

| Stage | Materials |
|-------|-----------|
| 1 | Water-Driven Mill, a shovel (kept) |
| 2 | 40 Stone, 40 Mud Brick, 10 Wooden Planks |
| 3 | 10 Clay, 1 Bellows, 10 Wooden Planks |
| 4 | 20 Wooden Planks, 10 Mud Brick, 10 Plaster, 2 Iron Parts, 2 Iron Bearings, 1 Iron Axle, 10 Metal Nails*, 1 Tin Solder* |

**Heat and fuel**

- Temperature goes up to 1,800. With no fuel the forge cools by about 33 degrees every 15 minutes, and above 1,000 it loses another 100 unless wind blows (a windy location or a recent Blast).
- Fuel holds 96. Firewood and other fuel add 20, Charcoal 25 and Embers 16. The forge only gains heat while it holds fuel.
- **Blast** (1 hour) adds 480 Temperature and burns 16 fuel. It needs a lit forge with fuel and leaves a pile of Ash. Blast heats only the forge.

**What it does**

- **Smelt Ore:** drag Greenstone onto the hot forge. It gives one copper Metal Nugget at the ore's Quality, plus one Greenstone Slag.
- **Smelt Greenstone:** Greenstone Powder becomes one copper Metal Nugget at the powder's Quality, plus one Greenstone Slag.
- **Iron parts** (Iron Parts, Bearing, Axle, Wrench) smelt at 1,100 or more. A Bearing, Axle or Wrench gives 6 iron nuggets and Iron Parts give 3, at the quality of the bar they came from.
- The forge copies the vanilla kiln and smelting recipes, so clay fires and vanilla ores smelt in it.
- **Blast with Advanced Copper Tools:** every ACT metal item inside melts into nuggets of its own Metal Type: Iron 200, White Bronze 140, Tin Bronze 130, Tin 120, Ghost Bronze 110, copper 100. Each item's nuggets take that item's own quality. Items with no quality of their own, such as armour and shields, give quality 0.
- **Fire-tending:** a Partner can tend the fire with vanilla's duty.
- **Operate the Forge** (Partner duty): the Partner smelts ore at a forge you have already heated. It never lights or blasts it.

### Water-Driven Workshop (upgrade)

Adds batch metalworking and 5,000 weight of storage to the forge.

**Build** (research 8 hours, 2 hours): Water-Driven Forge Kit, Water-Driven Mill, Forge Hammer, 10 Charcoal, 2 Iron Parts, 2 Iron Bearings, 1 Iron Axle, 1 Iron Wrench, 2 Small Copper Gears, 1 Large Copper Gear, 8 Metal Nails*, 1 Tin Solder*.

The Workshop keeps every forge action and adds these:

- **Hammer All** (30 minutes): hammers everything inside at once, raising Metal Quality and Quality by 2 a press, up to 60. Past 60, only the game's own hammering raises them.
- **Hammer Copper Sheet** (1 hour): 6 copper nuggets make 1 Metal Sheet at the nuggets' average Metal Quality.
- **Forge Copper Nails** (1 hour): 6 copper nuggets make 6 copper Metal Nails.
- **Forge Iron Nails** (1 hour): 6 iron nuggets make 6 Iron Nails (Metal Nails at Metal Type Iron).
- **Hammer Iron Sheet** (1 hour): 6 iron nuggets make 1 Metal Sheet at Metal Type Iron, at the average Metal Quality.
- **Cast Metal Lump** (1 hour): 6 copper nuggets make one Unfinished Metal Bar at their average quality, which Hammer All can then work.
- **Expand** (30 minutes): rebuilds it as the **Expanded Water-Driven Workshop**, holding 7,000 instead of 5,000. Temperature, contents and a fitted wheel stay. Cost and rules match the sawmill's Expand.
- **Operate the Workshop** (Partner duty): Hammer All, then Smelt Ore, at a Workshop you have already heated. The expanded Workshop has its own duty.

Iron nuggets are vanilla Metal Nuggets at Metal Type 200. The Ore Sluice makes them, and so does smelting a WDI iron part. Each craft takes only nuggets of its own metal.

The Workshop's heat stands in for the heated bar the sheet blueprints ask for. The 6-nugget costs may change.

---

## Water-Driven Grinding Mill

The wheel drives the millstone and automates grinding.

**Build** (research 24 hours, 2 hours, needs a Water-Driven Mill): Water-Driven Mill, Rotary Quern, 20 Wooden Planks, 10 Stone, 1 Iron Bearing, 1 Iron Axle, 8 Metal Nails*, 1 Tin Solder*.

It takes anything you can grind, crush or pound: grains, acorns, dried herbs and mushrooms, Bone Splinters (into bonemeal), Charcoal, Greenstone and the bog irons. 

Other hammer jobs stay out, such as breaking a cast, bones or a mud brick, mashing berries, cracking a geode, or making clay from mud.

**Grind All** takes 30 minutes, or 15 with an Overshot Wheel. **Operate the Grinding Mill** (Partner duty): the Partner presses Grind All, but only while a marked mill holds something to grind.

---

## Water-Driven Stamp Mill

Water-driven stamps crush a whole hopper of ore at once.

**Build** (research 24 hours, 2 hours, needs a Water-Driven Mill): the Grinding Mill's recipe with 2 Iron Sheets (Metal Sheets at Metal Type Iron) in place of the Rotary Quern.

**Hopper** (18 slots, 8,000 weight): Greenstone, Greenstone Slag and Bog Iron (fresh, dried or roasted), and nothing else. Tin ore does not go in.

**Stamp All** (30 minutes) crushes the hopper into Greenstone Powder, Greenstone Slag Powder or Crushed Bog Iron. Each comes out at the ore's Quality plus a hammer bonus: Greenstone +7, Bog Iron +3, slag +0.

The Grinding Mill also crushes these ores, with no bonus. A good quern still beats the stamp mill on Greenstone. The recipe and bonus may change.

**Operate the Stamp Mill** (Partner duty): the Partner presses Stamp All, but only while a marked mill holds ore.

---

## Water-Driven Fulling Mill

Water-driven trip hammers beat a whole batch of finished cloth at once to raise its quality. The base game sets a cloth's quality at the loom and never improves it afterward; the fulling mill is the one way to raise it.

**Build** (research 24 hours, 2 hours, needs a Water-Driven Mill): Water-Driven Mill, 20 Wooden Planks, 4 Heavy Stone, 2 Iron Parts, 8 Metal Nails*. Researchable once a Water-Driven Mill is on the board.

**Hopper** (12 slots, 3,000 weight): Small, Medium and Large Cloth only.

**Full All** (1.5 hours) beats every loaded piece, raising each one's Quality by 5. It stops at 50 (a cloth's Quality caps at 60), so fulling brings poor and fair cloth up to a fair finish and the last of a piece's quality still comes from better fibre at the loom. One press is one step for the whole batch; repeat to keep improving.

No Partner duty and no Overshot Wheel. The gain per press and the ceiling are placeholders and may be rebalanced.

---

## Ore Sluice

Flowing water washes minerals out of soil.

**Build** (needs a Mill Race, each part researched in 4 hours):

| Step | Materials |
|------|-----------|
| Ore Sluice Frame (1 hour) | 12 Wooden Planks, 6 Metal Nails*, a hammering tool (kept) |
| Ore Sluice (1.5 hours) | 10 Wooden Planks, 4 Heavy Stone, 1 Ore Sluice Frame, 6 Metal Nails* |

Place it next to a Mill Race.

Metal is rare: a mud pile gives a copper nugget about 1 time in 50 and greenstone 1 in 33. Dirt and fine dirt give less, and tin and iron less still. The sluice supplements the game's copper mine, it does not replace it.

Nuggets come out at Metal Quality 35.

A pile whose rolls all miss leaves a **Stone**, so Sluice All never comes out empty.

**Sluice All** takes 45 minutes, or 15 with an Overshot Wheel. **Operate the Ore Sluice** (Partner duty): the Partner presses Sluice All, but only while a marked sluice holds a Mud, Dirt or Fine Dirt pile.

---

## Fishpond

A dug and stocked pond for a steady supply of fish.

**Build** (research 16 hours, five 2-hour stages, needs a Mill Race):

| Stage | Action |
|-------|--------|
| 1 to 3 | Dig with a shovel (25 durability each) |
| 4 | Line: 10 Wooden Planks, 4 Metal Nails*, 3 Bugs, 15 Heavy Stone, 30 Stone |
| 5 | Stock: 2 Pike, 2 Perch, 2 Minnow |

The population grows slowly, about one Pike, Perch or Minnow every 3.5 days. Harvest it with the **Catch** buttons (1 hour each, 30 minutes for Crayfish and Frog). Each needs at least one whole fish of that species.

**Frogs:** the Stocked pond's **Catch Frog** takes a frog from a pool your ponds share: up to 6 frogs, and one more arrives each day. With none left, Catch Frog cannot be used until one arrives.

In winter the pond becomes the Fishpond (Frozen), with Ice Fish buttons, and cannot be packed up until spring.

- **Rod and spear:** drag a fishing rod onto a pond to **Fish**, or a spear to **Spearfish**. Each takes 30 minutes, wears the tool as river fishing does, and gives 0.5 Fishing XP.
- **What you catch:** a random species, weighted by how many the pond holds, at Quality 72. Crayfish are not caught this way. With no fish, nothing happens and no time passes.
- **Partner fishing:** mark a pond with **Fish the Fishpond** in the Duty Assignment menu. The Partner presses the Catch buttons under the same rules, taking the first species still in the pond (Pike, Perch, Minnow, Sturgeon, Trout, Char, Eel, Crayfish, Frog). Mark the pond again if it switches between Fishpond and Fishpond (Stocked).
- **Eel:** drag a vanilla Eel onto a pond and **Stock Eel** adds it, up to 10. Eels do not breed. **Catch Eel** (spear in reach) or **Ice Fish Eel** takes one out. Eels survive the freeze and thaw.
- **Feeding:** drag Fish Scraps, Bugs or any fishing bait onto a working pond, and **Scatter Fish Food** adds a quarter fish to the smallest breeding species among Pike, Perch and Minnow (2 to 9 fish). Your ponds take one feed a day between them, and with no species breeding the food is wasted. The frozen pond cannot be fed.
- **Kit:** Pack Up keeps the fish. A kit from **Drain**, or the Angler's Start kit, places as an empty Fishpond to stock again.

---

## Fish Funnel

A woven timber wing-wall staked across a river to steer fish into a trap.

**Build** (research 4 hours, 1.5 hours, needs a Mill Race): 6 Wooden Planks, 2 Rope, 4 Twine. It gives a **Fish Funnel Kit**. Place it at a river, and **Pack Up** returns the kit.

Disassembling the kit gives back 3 Wooden Planks, 1 Rope and 2 Twine.

A placed funnel doubles how fast a vanilla Funnel Trap at the same location gains fish. It catches nothing alone, so you still need the trap in the same water. A second funnel at the same location adds nothing. The rate may be rebalanced.

---

## Reed Bed and Cattail Bed

Plantable copies of the wild reed and cattail patches, in **Farming > Agriculture**.

| Blueprint | Research | Build (1 hour) | Each harvest gives |
|-----------|----------|----------------|--------------------|
| **Reed Bed** | 2 hours, needs a Mill Race | 6 Fresh Reeds | 4-8 Fresh Reeds |
| **Cattail Bed** | 2 hours, needs a Mill Race | 3 Cattail Roots | 2-4 Fresh Reeds, 1 Cattail Root, and Cattail Fluff in autumn and winter |

- Build at a river or lake, or at any outdoor location a connected outlet waters. Anywhere else the build is refused.
- A bed regrows like the wild patch. A harvest takes about 3 days of growth, faster in spring and none in winter. A new bed has one harvest ready.
- Harvest by hand (30 minutes), or all at once with a metal scythe or sickle. **Clear Bed** pulls the bed up and the plants are lost.
- Only planting needs water. Harvesting is never blocked.
- **Harvest the Reed Bed** and **Harvest the Cattail Bed** (Partner duties): the Partner harvests a marked bed whenever 5% of its growth is back.

---

## Water-Source Tag and Plumbed Bathtub

**Plumbed bathtub** (needs Advanced Copper Tools): an empty placed Copper Bathtub where a Mill Race Outlet runs fills itself within seconds, ready for a Cold Bath. A frozen or closed outlet fills nothing, and a warm tub is left alone.

Another mod can also mark a card as a water source so WDI structures can be built there (see **For modders**).

---

## Water Basin

A low stone basin fed by a thin stream. While it stands at your location it adds 10 to your Sanctuary. It is in **Construction > Furniture**.

- **Research:** 2 hours, needs a Mill Race. **Build** (1.5 hours): 8 Stone and 1 Metal Sheet of any metal but iron.
- Build at a river or lake, or at an outlet-watered outdoor location, never indoors. One basin per location.
- **Clear Basin** (30 minutes, asks to confirm) removes it. Nothing comes back.
- Only building needs water. A placed basin keeps its Sanctuary through a frozen outlet or after the outlet is gone. The +10 may be rebalanced.

---

## Retting Pit

A stone-lined pit kept full by running water, so you can ret flax, nettle and reeds at your base instead of carrying them to a river. It is in **Construction > Furniture**.

- **Research:** 2 hours, needs a Mill Race. **Build** (1 hour): 6 Stone, 4 Wooden Planks and 2 Rope. It gives a **Retting Pit Kit**; place it, and **Pack Up** (30 minutes) returns the kit. Disassembling the kit gives back 3 Stone, 2 Wooden Planks and 1 Rope.
- Build and place it at a river or lake, or at an outlet-watered outdoor location, never indoors, the same rule as the Sawmill. Anywhere else the build is refused.
- Drag **Flax Stems**, **Nettle Stems** or **Fresh Reeds** onto it to soak them. They turn into the same Soaking cards a river makes and gain quality exactly as they do there: no faster and to the same limits (flax 40, reeds 50, nettle 30). Nothing else can be soaked in it.
- The pit takes no stems while its water is cut off: a closed or missing outlet, or a frozen one with no fire beside it. Stems already soaking carry on.
- The build cost may be rebalanced.

---

## Irrigation Channel

A plank-lined channel that carries water from a Mill Race Outlet along the field edges. It only defends crops against a dry spell: it never makes them grow faster. It is in **Farming > Agriculture**.

- **Research:** 2 hours, needs a Mill Race. **Build** (1 hour): 4 Stone and 4 Wooden Planks. It gives an **Irrigation Channel Kit**; place it, and **Pack Up** (30 minutes) returns the kit. Disassembling the kit gives back 2 Stone and 2 Wooden Planks.
- Build and place it where the Sawmill can stand (a river or lake, or an outlet-watered outdoor location, never indoors). One channel per location.
- While a flowing Mill Race Outlet stands at the same location, each Flax, Wheat, Pea and Turnroot field there, growing or mature, gains 20 Hydration a tick. A field loses Health below 1920 Hydration; the channel lifts it back over that line, but above 1920 the field dries faster (24 a tick, 36 when very wet) than the channel adds, so on its own the channel never wets a field past about 1920. Growth, yield and Health are not touched.
- It feeds nothing while the outlet is closed or frozen (a fire does not help here), or when no outlet stands at the location. Fields on a river shore or in wetlands already gain 20 Hydration from the ground, so the channel leaves them alone.
- Only the four vanilla field crops are watered. Garden plots, planted crops and other mods' plants (Herbs and Fungi, Far Territory, Harsh Wilds) are not.
- The 20 a tick and the build cost may be rebalanced.

---

## Gears, iron parts and fasteners

Advanced machines need copper gears, a copper saw blade and iron parts. Cast the copper pieces from a molten crucible and forge the iron pieces from heated iron bars.

| Blueprint | Research | Build | Ingredients |
|-----------|:--------:|:-----:|-------------|
| **Cast Large Copper Gear** | 4 hours | 1 hour | Large Molten Crucible of copper, 3 Mud Brick, 3 Clay, hammering tool (kept) |
| **Cast Small Copper Gear** | 2 hours | 45 min | Small Molten Crucible of copper, 2 Mud Brick, 2 Clay, hammering tool (kept) |
| **Cast Large Bronze Gear** (ACT) | 4 hours | 1 hour | Large Molten Crucible at Metal Type 120-140, 3 Mud Brick, 3 Clay, hammering tool (kept) |
| **Cast Small Bronze Gear** (ACT) | 2 hours | 45 min | Small Molten Crucible at Metal Type 120-140, 2 Mud Brick, 2 Clay, hammering tool (kept) |
| **Cast Copper Saw Blade** | 4 hours | 45 min | Large Molten Crucible of copper, 10 Mud Brick, 10 Clay, hammering tool (kept) |
| **Forge Iron Parts** | 2 hours | 30 min | 1 Heated Metal Bar (iron) gives 2 Iron Parts |
| **Forge Iron Bearing, Axle, Wrench** | 2 hours | 45 min each | 1 Heated Metal Bar (iron) and a hammering tool (kept) |

- **Bronze gears** carry the crucible's Metal Type (tin 120, tin bronze 130, white bronze 140) and smelt back into nuggets of that type. They fit wherever a copper gear of the same size is needed: Water Wheel, Overshot Wheel, Water-Driven Mill, Sawmill and Workshop Kit. They give no bonus.
- **Copper gears and the saw blade** smelt back into copper nuggets at the quality of the crucible they were cast from, in a furnace or the WDI Forge at 1,100 or more. A piled stack of bronze gears can be spread out to pick one metal type.
- **Iron parts** smelt in the WDI Forge or Workshop at 1,100 or more, not in a vanilla furnace. Parts made before 1.12.3 have no recorded quality and melt at 0.

**\* Fasteners and sheets.** Every nail slot takes the framework's Metal Nail of any metal, and every solder slot takes its Tin Solder. Nails come from the framework's nail blueprints or the Workshop. Tin Solder comes from Forge Tin Solder: one tin nugget makes 2.

Sheet slots read the sheet's metal: the Stamp Mill takes Iron Sheets and the Water Basin any sheet but iron. Iron Sheets come from the framework's Iron Sheet blueprint (6 iron nuggets) or the Workshop's Hammer Iron Sheet.

**Retired parts (1.12.3).** WDI no longer makes its own Cast Copper and Iron Rivets, Alloy Solder or Cast Copper and Iron Sheets. They duplicated the shared cards under near-identical names, and the cast sheets could not be melted back.

Their six blueprints, Cut Iron Rivets from Sheet included, have left the journal.

An old card in a save is named "(old)" and turns into the shared card within a quarter of an hour, or at once with its Reforge button.

Copper becomes a Copper Sheet or Copper Nail, iron an Iron Sheet or Iron Nail, and Alloy Solder a Tin Solder.

---

## Skill training

Building and working WDI trains vanilla skills, matching the vanilla equivalents:

| What | Skill gained |
|------|--------------|
| Casting and forging parts (gears, saw blade, iron parts) | Smithing +1.0 each |
| Water Wheel, Mill, Overshot Wheel, Sawmill, Grinding Mill, Stamp Mill, Fulling Mill, Forge, Workshop Kit, Ore Sluice, Fishpond | Crafting +2.0 |
| Mill Race, Outlet, Sluice Frame, Fish Funnel, Water Basin, Retting Pit, Irrigation Channel | Crafting +1.0 |
| Water Wheel, Mill, Overshot Wheel, Mill Race, Sawmill | Woodworking +0.5 as well |
| Reed Bed, Cattail Bed | Herbalism +1.0 |
| Sawmill Cut (per log) | Woodworking +0.5 |
| Pond Catch or Ice Fish (per fish) | Fishing +0.5 (Crayfish and Frog +0.25) |

The batch buttons (Grind All, Hammer All, Sluice All, Saw All, Blast) give no XP. Skill Speed Boost multiplies these gains like any other.

---

## Character creation perks

All perks are in the **Situational** tab.

| Perk | Cost | Starting items |
|------|-----:|---------------|
| **Forge Start** | 1 Star | Water-Driven Forge blueprint (researched), 1 Mill Race, 1 Large Copper Gear |
| **Sawmill Start** | 2 Moons | Water-Driven Sawmill blueprint (researched), 1 Copper Saw Blade |
| **Grinding Mill Start** | 1 Star | Water-Driven Grinding Mill Kit, 1 Mill Race |
| **Prospector's Start** | 2 Moons | Ore Sluice Frame |
| **Angler's Start** | 2 Moons | Fishpond Kit (no fish: stock it yourself) |
| **Homesteader's Waterworks** | 1 Moon | Mill Race Outlet (packed), 1 Mill Race |
| **Millwright** | 15 Suns | +75 Woodworking head start |

---

## Partner duties

Assign these to a recruited Partner in the Duty Assignment menu:

- Operate the Grinding Mill, Ore Sluice, Sawmill, Expanded Sawmill and Stamp Mill
- Operate the Forge, Workshop and Expanded Workshop
- Harvest the Reed Bed and Harvest the Cattail Bed
- Fish the Fishpond (one duty each for the Fishpond and the Fishpond (Stocked))

A Partner works only the stations you mark. The Grinding Mill, Ore Sluice, Stamp Mill, Workshop and Expanded Workshop duties run at most 4 times a day per Partner. The Forge and Workshop also take vanilla's fire-tending duty.

---

## Installation

**Requirements:** BepInEx 5.x, CSFFModFramework 2.28.0 or later, and Card Survival: Fantasy Forest (EA 0.69c). Advanced Copper Tools is optional: it adds Blast melting for its metal items, the lit Copper Brazier as winter heat, bronze gears and the plumbed bathtub.

1. Install BepInEx 5.x if you have not already.
2. Install CSFFModFramework to `BepInEx/plugins/CSFF_Mod_Framework/`.
3. (Optional) Install Advanced Copper Tools to `BepInEx/plugins/Advanced_Copper_Tools/`.
4. Extract this mod to `BepInEx/plugins/Water_Driven_Infrastructure/`.
5. Launch the game and look for `WaterDrivenInfrastructure v1.12.12 loaded.` in `BepInEx/LogOutput.log`.

---

## Settings

BepInEx writes `BepInEx/config/crispywhips.waterdriveninfrastructure.cfg` on first launch. An edit to the file needs a full game restart, not a save reload. The optional [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) (BepInEx 5 build) changes it in game instead, and that applies at once: press F1 and find "WaterDrivenInfrastructure".

- **Seasons > Winter Station Lock** (default `true` since 1.12.10; BepInEx keeps a value already in your file, so an existing install stays as it was): when on, the water-powered stations stop in winter everywhere, even beside a river, unless a heat source from the winter heat list stands there. It never blocks packing up, fuel, lighting, smelting or a pond's own freeze.
- When off, only a frozen outlet stops a station in winter, and stations at a river or lake work all year.

---

## Compatibility

- Advanced Copper Tools is optional and not a plugin dependency, so load order does not matter.
- Works alongside Herbs and Fungi, Repeat Action, Skill Speed Boost, Sirus23 Mod Collection and other framework mods.
- Safe to add to an existing save. Removing it mid-save makes its structures disappear but does not corrupt the save.

---

## Testing status

Most features were checked by scripted tests in a running game. Not yet tried in a game, or by hand:

- The 1.12.3 fixes (nugget quality, Blast, Cast Metal Lump, Hammer All on an Iron Bloom, world temperature, Smelt Greenstone, retired parts turning into shared ones). They have scripted checks that are not yet confirmed by hand.
- The Ore Sluice's new metal rates, the Iron Nail cost of Expand, and the Iron Sheet recipes for the Stamp Mill and Water Basin.
- The animated Water Wheel art: seen working, not yet judged by eye.
- The animated Sawmill, Grinding Mill, Forge, Workshop and Overshot Wheel art (1.12.6): built and checked frame by frame, not yet seen in a running game.
- The pile toggle on Bronze Gears (1.12.5).
- Partner duties for the Stamp Mill, Fishpond and expanded stations: scripted checks only.
- Part of the 1.12.10 balance pass (the slower pond, once-a-day feeding, the empty drained pond, the Partner duty limit, Winter Station Lock on by default): its scripted checks in a running game are being repeated.
- The Irrigation Channel: checked against the data by scripted tests, not yet seen in a running game.

---

## Troubleshooting

- **Blueprints not appearing?** Check that CSFFModFramework is installed and the log shows `WaterDrivenInfrastructure v1.12.12 loaded.`
- **Forge won't smelt?** It must reach 1,100. Feed it charcoal and Blast first.
- **Outlet not producing water?** It must be outdoors with a Mill Race built first. In winter it freezes: wait for spring, or keep a heat source there. Stations keep working, but a Rain Cistern stays unfed.
- **Rain Cistern not filling?** It must stand at the same location as a running outlet and be empty or hold River Water.
- **Hammer All does nothing?** Load tool blanks or copper pieces into the Workshop first. The game never shows Metal Quality as a number, but the boost is real. It also raises the visible Quality of a copper nugget, and of other items once they have taken a Strike.
- **Sawmill Cut missing?** The sawmill must be placed, not held as a kit. Drag a log onto it.
- **Saw All does nothing?** Put Tree Logs into the sawmill's inventory first. Dragging a log onto the sawmill runs Cut instead.

---

## For modders

**Water-source tag:** give a card the tag `tag_x_WaterSource` and a WDI station, Reed Bed, Cattail Bed or Water Basin can be built at its location as if beside a river.

The tag affects building only. Drawing water and the cistern feed still need a Mill Race Outlet. WDI never ships the tag itself.

**Installed layout:**

```
BepInEx/plugins/Water_Driven_Infrastructure/
├── Water_Driven_Infrastructure.dll
├── ModInfo.json
├── BlueprintTabs.json
├── Data/MillRaceMapEdges.json
├── CardData/ (Blueprint, EnvImprovement, Gif, Item, Location)
├── CharacterPerk/
├── NPCDuty/
├── ScriptableObject/ActionTag/
├── Localization/ (SimpEn.csv, SimpCn.csv)
└── Resource/ (GIF, Picture)
```

**Code map:**

| Patch | Purpose |
|-------|---------|
| **GameLoadPatch** | Postfix on `GameLoad.LoadMainGameData`. Injects the Mill Race improvements into world-map locations, copies the vanilla kiln and smelting recipes into the forge and workshop, repairs the Greenstone powder smelt and fixes the iron smelting container tag. |
| **MillRaceNetwork** | Two-way race connectivity. Gates outlet and station placement, water draws and station use to locations with direct water or a connected, unfrozen outlet. A heat source bypasses the winter freeze. Also runs the optional Winter Station Lock. |
| **ActionInterceptPatch** | `ActionRouter` handlers for Grind All, Sluice All, Stamp All, Saw All (`AfterWrapped`, so its time is charged), pond stocking and catches, the Workshop crafts, Hammer All, Blast and Expand. Checks station inventory before consuming. |
| **FishpondPopulationPatch** | Gates breeding per species (needs 2) and swaps the pond between its Filled and Stocked cards at 10. |
| **BathtubPlumbingPatch** | With ACT, polls every 5 seconds and fills an empty Copper Bathtub at a running outlet. |
| **BronzeGearTabPatch** | With ACT, adds the two Cast Bronze Gear blueprints to Metal Crafts at runtime. |
| **MillDutyPatch** | Grafts the 12 `NPCDuty` files onto the vanilla Partner template. A postfix on `NPCDuty.RequiredActionsAreDoable` stops the mill, sluice and stamp mill duties while no marked station holds work. Every duty sets `RequiresMarkedCard`. Base weight 850. A postfix on `GameStatsToNPCStats.Init` adds the pond frog stat to `StatsStillAffectedByNPCS`, so a Partner's Catch Frog spends the pool. |
| **FishFunnelPatch** | Prefix on `InGameCardBase.AddExternalPassiveEffect`: a Funnel Trap that already carries one live Fish Funnel's boost refuses a second. |
| **BaseTemperatureHeal** | On `GameManager.OnGMInitialized`, resets a positive vanilla BaseTemperature base to 0, repairing saves warmed by Blast before 1.12.3. |

Patches filter on this mod's UniqueIDs. Apart from BaseTemperatureHeal, they never modify vanilla cards, drops or stats.

**Building from source:** the project compiles against the plain game `lib/Assembly-CSharp.dll`, the same file as `CSFFModFramework/lib/Assembly-CSharp.dll`. Copy the game's own DLL there after a game update.

---

## Version history

Full notes are in CHANGELOG.md.

- **1.12.12:** the new Retting Pit and Irrigation Channel; Hammer All raises copper nuggets; a Partner's Catch Frog spends the frog pool; a second Fish Funnel no longer speeds a Funnel Trap; Fulling Mill kit planks come back as oak.
- **1.12.11:** the new Water-Driven Fulling Mill raises the Quality of finished cloth; the Water Basin's animation sits where its still picture does; the Overshot Wheel's help text gives the right Saw All time.
- **1.12.10:** a balance pass closer to the base game: copper smelting gives a Metal Nugget and slag, Hammer All stops at 60, the sawmill is slower and its blade wears out, the fishpond breeds and pays out more slowly, frogs are limited, and every station stops in winter without heat by default.
- **1.12.8:** the winter Fishpond stays still again.
- **1.12.7:** calmer animated water: about half the speed and less strong.
- **1.12.6:** animated card art for the Sawmill, Grinding Mill, Forge, Workshop and Overshot Wheel.
- **1.12.5:** the Forge and Workshops no longer heat themselves with no fuel; Bronze Gear piles can be spread out.
- **1.12.3:** Blast no longer warms the world, melted metal keeps its own quality, Partners only work marked stations, and WDI's own nails, solder and sheets give way to the shared ones.
- **1.12.2:** Expand for the Sawmill and Workshop, Partner fishing, bronze gears, an animated Water Wheel.

---

## Credits

- **Author:** Jared (crispywhips)
- **Framework:** [CSFFModFramework](https://github.com/jscott3/CSFF_Mods) + BepInEx and Harmony
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games
