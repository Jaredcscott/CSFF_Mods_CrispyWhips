# Far Territory

**Version:** 0.1.0
**Author:** Jared
**Requires:** CSFFModFramework 2.28.0 or later (a hard requirement: the map is added through the framework's `WorldMap/MapNodes.json`)
**Status:** pre-release. This build adds the coast at the end of the river (three new places and a few cards), the sea and a tide pool on Seaward Sands, the boat crossing to the island's first beach, the island's warmth, the island's whole coast (seven shore places and Gull Stack, a rocky islet you reach by swimming), and its whole interior: jungle, grassland, marsh, deep wood, a hidden valley, a misty ridge, the uplands, a volcano with its acid lake, a shipwreck and an old ruin, and sixteen caves, eight of them a chain of dark halls under the island; the first far land, a grassland west of the forest with its moose; the mountains north of the forest, with a hot spring, rock salt, three caves and a cliff climbed with rope and pitons; and the fjords beyond the mountains, with seals, walrus, puffins and whales offshore. Every card has its own picture (only the River Mouth's paper-map picture and the remedy effects' status icons are the base game's), and every number below is a tunable placeholder, not a balanced value.

Far Territory adds lands beyond the forest, reached by ordinary travel: an island recreating Card Survival: Tropical Island's island as a tribute, with all text and art new, reached down the river to the sea, then the grassland, the mountains and the fjords.

## Feature overview

Counts are of shipped data files (checked against the mod folder for this version).

| System | What it adds | Section below |
|---|---|---|
| Map | 51 map nodes in `WorldMap/MapNodes.json` and 50 place (environment) cards: the river-end coast, the island (coast, interior, 16 caves), and the grassland, mountain and fjord lands | Getting there, The island so far, The grassland, The mountains, The fjords |
| Travel | Dugout Canoe (paddle downstream), Outrigger Canoe (sea crossing), compass hops, path cards, swims, climbs that can end in a fall, a Rope and Pitons cliff climb | The dugout canoe, Crossing to the island, Getting around the island |
| Climate | The island, the volcano, the mountains and the fjords each shift how warm you feel (no cards; the mod adjusts Temperature Perceived itself), plus Spring Warmth | The island is warm, The volcano is hotter, The mountains, The fjords |
| Cards | 93 item cards (CardData/Item) and 167 location and structure cards (CardData/Location): sea and shore cards, palms, plants, carcasses, caves, paths | Cards |
| Blueprints | 3: Dugout Canoe and Rope and Pitons (Construction, Advanced Tools) and Fish Trap (Hunting, Fishing) | The dugout canoe, The mountains, Cards |
| Fishing | Line, net, spear and trap fishing on each coast (every fishing tool is accepted), 10 hidden fish and shellfish population stats, dive shellfish | Cards |
| Plants and medicine | Coconut chain, 7 tropical food plants, 8 medicine and spice plants and 4 fibre and flowering plants, all also grown in the base game's Garden Plot; 6 remedy effect stats plus the Spring Warmth effect (7 effect stats in all) | Cards |
| Animals | 9 roaming animals (macaque, monitor lizard, cobra, seagull, bat, moose, seal, walrus, puffin) with 9 encounters, carcasses to butcher, and sea dangers (shark, sea krait, urchin) | Animals, Sea dangers, The grassland, The fjords |
| Declarative extras | `DropInjections.json` (the Trail West and Trail North forage finds, and the Harsh Wilds carcass finds), `BlueprintTabs.json`, one `GameSourceModify` text patch on this mod's own Brackwater Mouth card | Getting there, The grassland, The mountains |

**Code in the mod** (four small classes, one Harmony patch): `EnvSaveDataPreCreate` creates each non-clone board's save entry at run start; `ClimateService` applies the island, volcano, mountain and fjord temperature offsets from the place you stand on (see The island is warm); `LandmarkSeed` places Forest Lore's Standing Stones on Wildrye Plain once when Forest Lore is installed (see The grassland); `OwlIslandGuard`, the one Harmony patch, refuses the Sirus23 Mod Collection's Wild Owl a move onto any island board and does nothing without that mod (see The island is warm).

**Optional companions** (nothing breaks without them): Harsh Wilds (wisent and musk ox carcasses), Forest Lore (Standing Stones on Wildrye Plain), Sirus23 Mod Collection (its wild sheep can appear on island places), Advanced Copper Tools (its Grappling Hook also raises Climbing Aid). There are no configuration options; every number is a tunable value in the card JSON.

**Language:** English only (`Localization/SimpEn.csv`; no Chinese file yet).

## Installation

Download Far Territory from its [Nexus Mods page](https://www.nexusmods.com/cardsurvivalfantasyforest/mods/54), or install it from the Mod Update Manager suite, where it is listed as "Far Territory (Pre-release)". The source is on [GitHub](https://github.com/Jaredcscott/CSFF_Mods_CrispyWhips/tree/main/Far_Territory).

### Requirements
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Card Survival: Fantasy Forest
- CSFF Mod Framework 2.28.0 or later (`CSFF_Mod_Framework`)

### Steps
1. Install BepInEx 5 and CSFF Mod Framework.
2. Extract the `Far_Territory` folder into your `BepInEx/plugins/` directory.
3. Launch the game. An existing save works: see Notes at the end for which cards an older save gains and which need a new run.

Your plugins folder should look like:
```
BepInEx/
  plugins/
    CSFF_Mod_Framework/
    Far_Territory/
      Far_Territory.dll
      ModInfo.json
      CardData/
      WorldMap/
      ...
```

**License:** MIT (`LICENSE`). Every card picture is the mod's own; how each was made is in `ASSET_CREDITS.md`.

## Getting there

Follow the river south to Duck Point, its last bend. Duck Point now has a **South** exit. From there the coast runs east:

| Place | Where | What is there |
|---|---|---|
| **Brackwater Mouth** | south of Duck Point | The estuary: the river widens and turns brackish before the sea. It is still river water, so it keeps Duck Point's river (water and fishing) and its seasonal mosquitoes. A reed patch, a stone pile and a driftwood log. |
| **Driftwood Shore** | east of Brackwater Mouth | A low shore of shingle and wrack: a stone pile, a driftwood log, and a Forage action. |
| **Seaward Sands** | east of Driftwood Shore | A wide beach where the land runs out: shell-strewn sand, a driftwood log, a Forage action, the sea (wash, dive) and a tide pool. |

Each step is one ordinary compass hop. All three places appear on the paper map once visited.

### The dugout canoe

A **Dugout Canoe** takes you down the river in one go. Research its blueprint (Construction, Advanced Tools; 16 ticks, unlocked once a Tree Log is with you) and build it from a Tree Log and 2 Oak Bark with a cutting tool such as a flint knife, which it wears a little but does not use up (8 ticks).

- On any place with a flowing river (the main river from River Pass down to Duck Point, and other mods' river places that hold the same river card), the canoe offers **Paddle Downstream**: it carries you to Brackwater Mouth, the river's end, in 4 ticks (about an hour; tunable), costing 2 stamina a tick and wetting you a little.
- Walking from River Pass to Brackwater Mouth is 14 compass hops (14 ticks before any load slows you), so the canoe is faster from Mossy Bank and everywhere above it; from the last bends (Muddy Bank, Willow Ways, Clay Banks, Duck Point) walking is as quick or quicker.
- **Downstream only.** There is no paddle upstream: you walk back up. The button is absent away from a flowing river: on a lake, a marsh, a bog or a swamp, on the coast, and on Brackwater Mouth itself. In winter the river freezes and a river that has silted into marsh loses its flowing water, and the button goes with it.
- The canoe is heavy (as heavy as a tree log). Keep it with you to take it along: one set down on a river place stays there when you paddle off in another.

## Crossing to the island

An **Outrigger Canoe** lies on Seaward Sands, beside the **Misty Coast**. Its **Set Sail for the Island** action paddles you across the open water to **Landfall Strand**, the island's beach, far to the south-east. The canoe is there on Landfall Strand too, beside **Homeward Sand**, and there it offers **Set Sail for the Mainland**, which brings you back to Seaward Sands. Each beach has its own canoe and shows only the sail that leaves from it.

- Each crossing takes 24 ticks (6 in-game hours; tunable) and needs nothing: no tool, no stamina check, no season.
- The canoe is the only way across. There is no compass button between the two beaches, and the paper map draws no line across the sea, only Landfall Strand itself once you have landed there.
- **A Partner does not come with you.** A Partner following you stays behind on Seaward Sands when you sail, and does not find its own way across; it is still there when you sail back. Animals and other people cannot cross either: the sea route is closed to everyone's pathfinding.
- Landfall Strand holds shell-strewn sand, a driftwood log, a Forage action (stones and seashells, as on Seaward Sands) and the island's warm air (below). Its **South** exit leads to Coral Inlet.

## The island so far

| Place | Where | What is there |
|---|---|---|
| **Landfall Strand** | where the outrigger lands | Shell-strewn sand, a driftwood log, a coconut palm, Forage, Homeward Sand, the Outrigger Canoe. |
| **Coral Inlet** | south of Landfall Strand, one compass hop | A sheltered inlet behind the reef: the reef water (wash, dive), shell-strewn sand, a driftwood log, a coconut palm, Forage (seashells, seaweed and stones). |
| **Boulder Shore** | east of Landfall Strand, one compass hop | Weed-hung boulders and rock pools: The Rocky Swell (wash, dive), a tide pool, a coconut palm, Forage (stones, seashells, flint, seaweed). |
| **Palm Fringe** | the scrub behind Landfall Strand, reached by a short path (no time) | Where the beach meets the jungle: two coconut palms and a young palm, a driftwood log, Forage (long sticks, dry leaves, stones), and a path on into Vine Woods. No sea. |
| **Rootwater Thicket** | a long shore walk from Coral Inlet | A drowned wood of stilt-rooted trees: The Brackish Shallows (wash, grope among the roots for crabs and weed), a driftwood log, Forage (crabs, seaweed, long sticks, stones). |
| **Bleak Strand** | a long shore walk on from Rootwater Thicket | A long, empty grey beach: The Grey Surf (wash, dive), shell-strewn sand, a driftwood log, a tide pool, Forage (seashells, stones, flint, long sticks). |
| **Gull Stack** | an islet off the coast, reached only by swimming | A sheer gull rock: The Rocky Swell (the same water as Boulder Shore's), a tide pool, a coconut palm, a **Seagull Nest**, Forage (feathers, seashells, stones). |
| **Cliffbound Cove** | a hidden beach walled in by cliffs, reached by swimming from Gull Stack or by a climb down from Misty Ridge | Deep, calm water: The Cove Water (wash, dive), shell-strewn sand, Forage (seashells, stones, the odd crab), a climb up to Misty Ridge, an underwater passage into the Drowned Hall. |
| **Vine Woods** | east of Coral Inlet, one compass hop; a path from Palm Fringe | A tangled wood of vine-hung trees: Forage (long sticks, fibres, stones, mud), a path back to Palm Fringe. |
| **Westgrass Plain** | east of Vine Woods, one hop | Open pale grass: Forage (fibres, stones, a stick). |
| **Eastgrass Plain** | east of Westgrass Plain, one hop | The eastern grass: Forage (fibres, stones, twigs). |
| **Reed Marsh** | south of Vine Woods, one hop | Standing water, mud and reeds: Forage (mud, fibres, sticks, stones), a climb up a rock face to Misty Ridge. |
| **Green Deep** | south of Reed Marsh, one hop | The deep wood, dusk at noon: Forage (long sticks, fibres, mud). |
| **Hidden Vale** | south of Green Deep, one hop | A small closed valley: Forage (stones, sticks, fibres, mud). |
| **Misty Ridge** | east of Green Deep, one hop | A windswept ridge in cloud: Forage (stones, flint, sticks, fibres), climbs down to Reed Marsh and to Cliffbound Cove, a hop north to Western Uplands. |
| **Western Uplands** | south of Westgrass Plain, one hop; north of Misty Ridge | Open rolling hills of thin grass and boulders: Forage (fibres, stones, flint, sticks). |
| **Eastern Uplands** | east of Western Uplands, one hop; south of Eastgrass Plain | Higher, drier, stonier hills: Forage (stones, flint, sticks, fibres). |
| **Fire Mountain** | south of Eastern Uplands, one hop; a long walk from Bleak Strand | A black, steaming volcano: Forage (stones, flint), which makes it much hotter than the rest of the island. |
| **Sour Crater** | south of Fire Mountain, one hop | The acid lake in the mountain's shoulder, a still sour yellow-green pool: Forage (stones, mud, sticks); the heat of the volcano (see below). No water to wash or drink from, no poison in the air. |
| **Wreck Hollow** | a few steps from Reed Marsh (path card); off the paper map | A hollow in the hillside holding an old wooden ship, high and dry: the **Wrecked Ship**, Forage (sticks, fibres, stones). |
| **Fallen Dwelling** | a few steps from Hidden Vale (path card); off the paper map | The tumbled walls of an old settlement: Forage (mud, stones, fibres, sticks). |
| **Sea Grotto** | a cave off Boulder Shore (path card, no time); off the paper map | A low sea cave in the black rocks, wet and salt-crusted: Forage (stones, heavy stones, seaweed, crabs). Not dark. |
| **Wave Grotto** | a cave off Bleak Strand (path card); off the paper map | A wet cave where the sea runs in through a crack, a shallow pool floor: Forage (crabs, prawns, seaweed, sea urchins, stones). Wet, but with no tide to time. Not dark. |
| **Plain Grotto** | a cave off Westgrass Plain (path card); off the paper map | A shallow dry cave in a grassy bank: Forage (stones, bugs, bones). Not dark. |
| **Bat Roost** | a cave off Misty Ridge (path card); off the paper map | A tall cave with a roof of sleeping bats: Forage (niter crystals, bones, stones). Not dark. |
| **Monkey Den** | a den off Misty Ridge (path card); off the paper map | A dry hollow among the ridge roots where macaques sleep: Forage (bones, bird bones, feathers, rotten remains, coconuts). Not dark. |
| **Black Grotto** | a cave off Reed Marsh (path card); off the paper map | A cave mouth in the marsh bank that swallows the light: Forage (stones, bugs, heavy stones) needs a lit torch, lantern or brazier. **Dark**: the screen darkens and Light stays low until you carry a light. A way on leads to the Dripping Hall. |
| **Sinkhole** | a shaft in Western Uplands (a climb down and up, 1 tick); off the paper map | A fallen-in shaft with a rubble floor: Forage (stones, heavy stones, bones), and a way on into the Lightless Hall. Not dark. |
| **Lava Tube** | a tunnel off Eastern Uplands (path card); off the paper map | A smooth round tunnel in the black rock: Forage (stones, heavy stones, flint), and a way on into the Upper Hall. Not dark. |
| **Dripping Hall** | a few steps on from the Black Grotto (path card); off the paper map | A low wet chamber where water drips into a **Spring** (the base game's cave spring, which fills with water): Forage (stones, bugs, heavy stones). **Dark**. |
| **The Squeeze** | a quarter hour on from the Dripping Hall (path card, 1 tick); off the paper map | A tight crack in wet rock with a **Copper Vein** in its wall (the base game's own: mine it with a pickaxe for greenstone): Forage (stones, heavy stones). **Dark**. |
| **Lower Hall** | a narrow crawl from The Squeeze (2 ticks); a few steps from the Glittering Hall; off the paper map | The cave's long, low bottom chamber, with a Copper Vein: Forage (stones, flint, heavy stones). **Dark**. |
| **Glittering Hall** | a few steps from the Lower Hall; a climb up from the Drowned Hall; off the paper map | A high chamber crusted with pale crystal, with a Copper Vein: Forage (stones, the base game's Calcite Crystal, and now and then a Geode). **Dark**. |
| **Drowned Hall** | an underwater swim from Cliffbound Cove (2 ticks); a climb down from the Glittering Hall; off the paper map | A chamber half full of still sea water: Forage (crabs, prawns, stones, flint). **Dark**. |
| **Middle Hall** | a climb up from the Lower Hall or down from the Upper Hall; a few steps from the Lightless Hall; off the paper map | A chamber halfway down, with a Copper Vein: Forage (stones, flint, heavy stones). **Dark**. |
| **Upper Hall** | a few steps in from the Lava Tube; off the paper map | The highest chamber, where the lava tube gives way to broken rock, with a Copper Vein: Forage (stones, flint, heavy stones). **Dark**. |
| **Lightless Hall** | a few steps on from the Sinkhole; off the paper map | A small, black, silent chamber: Forage (a stone now and then, mostly nothing). **Dark**. |

### Getting around the island

- **Compass hops** (one tick each, a button on the location card): Landfall Strand to Coral Inlet (South) and to Boulder Shore (East); Coral Inlet East to Vine Woods; Vine Woods East to Westgrass Plain and South to Reed Marsh; Rootwater Thicket South to Westgrass Plain; Westgrass Plain East to Eastgrass Plain and South to Western Uplands; Western Uplands East to Eastern Uplands and South to Misty Ridge (the button is Misty Ridge's North); Eastgrass Plain South to Eastern Uplands; Eastern Uplands South to Fire Mountain; Fire Mountain South to Sour Crater; Bleak Strand West to Eastgrass Plain; Reed Marsh South to Green Deep; Green Deep East to Misty Ridge and South to Hidden Vale; each also back the other way.
- **Path cards** sit on the board and carry you with one action: **Path to Palm Fringe** and back (no time: it is a few steps), and the two long shore walks, **Path to Rootwater Thicket** from Coral Inlet and **Path to Bleak Strand** from Rootwater Thicket, each way (2 ticks; tunable), and **Path to Vine Woods** from Palm Fringe and **Path to Palm Fringe** from Vine Woods (2 ticks), **Path to Fire Mountain** from Bleak Strand and **Path to Bleak Strand** from Fire Mountain (2 ticks; the long walk up the black slope), and the short ways into the two hidden places: **Path to Wreck Hollow** from Reed Marsh and **Path to Fallen Dwelling** from Hidden Vale, each with a way back out (no time: a few steps). A path walk costs what a compass hop does.
- **Swims:** Boulder Shore, Bleak Strand and Cliffbound Cove each have a **Swim to Gull Stack** card, and Gull Stack has one back to each. A swim takes 2 ticks, needs daylight and some stamina (Light 10 or more, Stamina 12 or more), soaks you through (Wetness +100), costs 5 stamina a tick and trains your Swimming skill by 1 (all tunable). Unlike the island this recreates, the swim out to Gull Stack has no Swimming skill requirement yet. Gull Stack has no compass exits: swimming is the only way on or off it. Cliffbound Cove is reached by swimming from Gull Stack or by the climb down from Misty Ridge.
- **Climbs** (1 tick; tunable): **Climb Up to Misty Ridge** from Reed Marsh and from Cliffbound Cove, and **Climb Down** from Misty Ridge to Reed Marsh and to Cliffbound Cove. A climb needs some stamina (11 or more), costs 10 stamina and wears your hands and feet, and trains Climbing. It always gets you there, but can go wrong: each climb weighs a clean ascent (10, plus up to 150 for your Climbing skill and up to 50 for a climbing aid, the base game's own shaft numbers) against a fall that scrapes you (3) or bruises you (6); a fall lands the base game's fall event with its wound, adds filth, and still arrives. A tired climber (Stamina under 16) cannot make a clean climb at all. All of it is tunable.
- The paper map draws no line for a path, a swim or a climb. Each place appears on it once visited.
- **Caves**: each of the sixteen caves above is off the paper map and carries none of the island's climate (the caves keep the forest's temperature). The eight single caves are entered by a path card on their outside place (**Path to Sea Grotto** on Boulder Shore, and so on; no time, a few steps) and left by a card inside (**Path to Boulder Shore**); the Sinkhole is a climb (1 tick, the same rules as the other climbs) in both directions. The Black Grotto and the eight halls of the chamber chain (below) are dark: each holds a **Cave Darkness** card that raises the base game's *in a dark place* mark while you are there, so daylight no longer counts toward Light and Forage needs a lit light source; leave for an outside place and the mark drops again. The other seven single caves are not dark. Every forage table is a tunable first layer.
- **The chamber chain** (all tunable): eight dark halls under the island join the surface in four places, so you can walk underground between Reed Marsh, Cliffbound Cove, Western Uplands and Eastern Uplands.
  - From the **Black Grotto** a few steps on to the **Dripping Hall**, then a quarter hour (1 tick) to **The Squeeze**, and from there a **Narrow Crack** to the **Lower Hall** and back: 2 ticks, a crawl that costs 15 stamina, adds 30 filth and scrapes your hands (+10 hand damage), each way.
  - The **Lower Hall** is a few steps from the **Glittering Hall**, which has a shaft down to the **Drowned Hall**; from the Lower Hall a shaft climbs to the **Middle Hall**, and another from there to the **Upper Hall**, a few steps from the **Lava Tube**. The **Middle Hall** is also a few steps from the **Lightless Hall**, which is a few steps from the **Sinkhole**.
  - The shafts (Drowned Hall and Glittering Hall, Lower Hall and Middle Hall, Middle Hall and Upper Hall, each way) are climbs on the same rules as the other climbs (1 tick, Stamina 11 or more to start, a fall that scrapes or bruises and still arrives, a clean climb weighted by Climbing and a climbing aid, a tired climber under Stamina 16 sure to fall), except that a climb up costs 5 stamina and a climb down 10, as in the island this recreates, and each trains Climbing by a quarter. They need no daylight.
  - **Underwater Passage**: the **Drowned Hall** and **Cliffbound Cove** are joined by a swim under the rock, both ways: 2 ticks, Stamina 12 or more, Wetness +100, 5 stamina a tick, Swimming +1 (the Gull Stack swim's numbers), no Swimming skill needed and, unlike the Gull Stack swims, no daylight needed.
  - Every hall is dark, so bring a light to forage; the path cards themselves need none.

### The volcano is hotter

Fire Mountain and Sour Crater are hotter than the rest of the island: standing there raises how warm you feel by 16 (the island raises it by 9; tunable) and hides snow. There is no card for it. It touches nothing but how warm you feel, there is no poisonous air, and it goes away when you leave. Expected, not yet watched in play: a forest-winter body left on Fire Mountain for four hours climbs from 100 to about 115 (the base game's Sweating) and holds there, where on the beach it stays at 100; in summer it climbs further. The offset is a starting value to tune.

### The island is warm

Every island place is warmer than the forest whatever the season. There is no card for it: the mod adjusts the stats itself while you stand on an island place:
- While you stand on an island place, how warm you feel (the base game's Temperature Perceived) is raised by 9 (tunable), and your body temperature follows it through the base game's own bands. A forest winter's day becomes a comfortable one on the island: a body that would grow numb in the forest stays comfortable there, without sweating. Nothing new is added to keep track of.
- No snow shows on the island's places, even in winter.
- The warmth belongs to the place you are standing on. It stops the moment you leave the island and never reaches the forest: the island does not change the forest's temperature, thaw, weather or seasons.

What stays shared with the forest, because the game has one sky for the whole world: the weather shown at the top of the screen (a forest blizzard still shows as the current weather while you are on the island, though no snow lies there), the season and its name, and the length of the day. The +9 is added on top of the forest's own temperature, so the island is hotter in a forest summer than in a forest winter, and a winter night on the island is still chilly. The value is tuned to a measured forest-winter morning: the first value, +30, left a body sweating after four hours on the island.

No winter content spawns onto the island: the base game's winter changes only stats and the weather card. One other mod reaches it: with the Sirus23 Mod Collection installed, its wild sheep can turn up on an island place, because that mod puts them on whatever outdoor place you are standing on. The same mod's **Wild Owl**, which flies to wherever you are at night, is kept off the island: Far Territory refuses its move onto any island board, so the owl never turns up on a beach, in a cave or on the plains. Without the Sirus23 Mod Collection this does nothing. An owl already standing on an island board in an older save stays until dawn, when it roosts. This has not been watched through a real night on the island yet.

## Cards

- **Driftwood Log**: Haul Out turns it into a vanilla Tree Log (fragrance 18, fuel 35, quality 96, oak: the base game's floating-log values; tunable).
- **Shell-Strewn Sand**: Comb the Sand gives 1 to 3 Seashells. It holds 1152 points of shells, each comb takes 288 and needs 25 percent left, and it refills by 1 per tick (about 3 days per comb; tunable).
- **Seashells**: drag a Stone onto them to Grind them into vanilla Bonemeal, the garden and field fertilizer (2 ticks; tunable).
- **The Sea** (Seaward Sands): **Wash in the Sea** (1 tick, the base game's river wash numbers) and **Dive for Shells** (2 ticks, needs stamina, costs 4 stamina a tick and wets you). A dive brings up 1 to 3 Seashells (weight 3), 1 to 2 Seaweed (weight 2) or nothing (weight 3). It has no limit besides your stamina; there is no salt water or tide. Fishing and the shellfish are below. Tunable.
- **Tide Pool** (Seaward Sands): **Forage the Pool** gives a Shore Crab (weight 2), 1 to 2 Rock Prawns (weight 3) or 1 to 2 Seaweed (weight 2), and needs daylight and stamina. The pool holds 960 points of Catch, each forage takes 240 and needs 20 percent left (so four forages from full), and it restocks by 1 per tick (about 2.5 days per forage; tunable) while you are on the beach.
- **Seaweed**, **Shore Crab**, **Rock Prawn**: each has an **Eat Raw** button giving a little food (Seaweed 8 satiation, crab 24, prawn 16; tunable). They spoil in 2 to 3 days into the base game's rotten remains. They have no cooked form yet.
- **Fishing** (every sea card): every fishing tool works on the sea. Drag any fishing line or rod onto the sea card to **Fish with a Line**: the base game's **Fishing Line**, its unused **Fishing Rod**, or another mod's rod such as Community Mod Chest's **Iron Fishing Rod** (any card with the base game's fishing-line tag; 2 ticks, trains Fishing; the rod or line loses 1 use, nothing is consumed). A cast brings up nothing (weight 160, falling by 100 as Fishing rises to 150) or one of that coast's fish, each weighted by how much of that fish the sea still holds: **Seaward Sands** Goatfish 40, Parrotfish 40, Silver Herring 20; **Coral Inlet** (the richest) Goatfish 56, Parrotfish 56, Bonefish 48, Reef Grouper 32, Silver Herring 24; **Boulder Shore** (and Gull Stack) Goatfish 32, Parrotfish 32, Bonefish 24, Reef Grouper 12; **Bleak Strand** Goatfish 32, Parrotfish 24, Bonefish 24, Silver Herring 24; **Cliffbound Cove** Goatfish 32, Parrotfish 32, Bonefish 24, Reef Grouper 8; **Rootwater Thicket** Goatfish 24, Parrotfish 24, Bonefish 24, Threadfin 40, Mudskipper 40. All weights are at a full sea and tunable. Drag a fishing net (Community Mod Chest's **Fishing Net**, or any card tagged `tag_FishingNet`) onto the sea to **Cast the Net**: the same cost, odds and fish as the line, but a catch lands two of that fish (and takes two fish from its population); the net loses 1 cast.
- **Spear Fish** and **Set the Fish Trap** (the calm, shallow coasts only: Seaward Sands, Coral Inlet, Rootwater Thicket and Cliffbound Cove, whose sea cards carry the mod's own shallow-water tag): drag any spear (the base game's rustic, flint, obsidian and metal spears, or another mod's, such as Community Mod Chest's Fire-Hardened Spear) onto the sea to **Spear Fish** (2 ticks; needs daylight and stamina; trains Spear Fishing; the same fish as the line except Silver Herring and Mudskipper, with "nothing" at weight 160 falling by 100 as Spear Fishing rises). Drag a **Fish Trap** onto the sea to **Set the Fish Trap**, or the base game's **Funnel Trap** to **Set the Funnel Trap** (1 tick; stamina 10 or more).
- **Fish populations**: each species has a hidden population stat (`ft_pop_<species>`, never shown) that sets how likely it is to bite and that every catch lowers by 1000 (a Goatfish sea starts at 60000 and regrows 2 a tick, so a catch is made good in about 500 ticks; Reef Grouper and Threadfin 30000 and 1 a tick; Mudskipper 15000 and 2; Conch 30000 and 1; Oyster 30000 and 0.5; Sea Urchin 6000 and 0.5; all tunable). The populations are the mod's own: no base-game stat is read or written. They are shared by every coast.
- **Sea fish and shellfish**: **Goatfish**, **Parrotfish**, **Bonefish**, **Silver Herring**, **Reef Grouper**, **Threadfin** and **Mudskipper** (eaten raw for 12 to 50 satiation; spoil in 2 days), and **Conch**, **Oyster** and **Sea Urchin** from the dive (eaten raw for 12 to 20 satiation; spoil in 2.5 days). The dive now also brings up shellfish by coast, weighted by their population stats: Seaward Sands Conch and Sea Urchin; Coral Inlet Conch, Sea Urchin and Oyster (the reef); Boulder Shore and Cliffbound Cove Conch and Sea Urchin; Bleak Strand Conch. None has a cooked form yet.
- **Fish Trap** (Hunting, Fishing blueprint; 8 ticks to research, 6 to build from 4 Twigs and 3 Twine) and **Fish Trap (Set)**: the set trap springs every 96 to 120 ticks and rolls the shallow coast's fish by their populations (and nothing, at weight 120 falling with Fishing); each catch lowers its species' population. **Haul Out the Trap** (1 tick) turns it back into the carried trap with what it caught, which keeps fresh while inside. Either trap holds up to four fish (four slots, 4000 weight; tunable). The trap never wears out (tunable). **Funnel Trap (Set)** catches exactly like the set Fish Trap; **Haul Out the Trap** gives back the base game's Funnel Trap and spills its catch onto the shore beside it (the base game's trap only holds Perch, so the sea fish cannot ride inside).
- **Outrigger Canoe** (Seaward Sands and Landfall Strand): the boat of the crossing (above), one on each beach. It cannot be trashed, and a missing one comes back the next time you arrive on its beach.
- **Misty Coast** (Seaward Sands) and **Homeward Sand** (Landfall Strand): the two shores the crossing leaves from, places with no actions of their own. Neither can be trashed, and a missing one comes back the next time you arrive on its beach. (Before the Outrigger Canoe they were the two boats, the Moored and the Beached Outrigger, and the sails were theirs.)
- **The Reef Water** (Coral Inlet): the same two actions as The Sea, with the same numbers (tunable). Its fish and shellfish are the richest on the island (see Fishing below).
- **The Rocky Swell** (Boulder Shore and Gull Stack), **The Grey Surf** (Bleak Strand), **The Brackish Shallows** (Rootwater Thicket) and **The Cove Water** (Cliffbound Cove): the same Wash in the Sea as The Sea, fishing with each coast's own fish (below), and a dive with each coast's own finds: seashells, seaweed and stones off the rocks; seashells, seaweed and flint past the surf; crabs and seaweed among the roots; seashells and seaweed in the cove. Same costs as The Sea's dive (tunable).
- **Coconut Palm** (Landfall Strand, Coral Inlet, Boulder Shore, Gull Stack, and two on Palm Fringe): it holds up to 4 coconuts and grows them back at 1 per 720 ticks (about 7 days each, 30 days to refill from empty; tunable). **Climb the Palm** (2 ticks, needs a coconut ripe, daylight and stamina 11 or more) twists one loose: each climb weighs a clean climb (10, plus up to 150 for your Climbing skill and up to 50 for the Climbing Aid stat, but a climber with under 16 stamina is sure to fall) against a fall that scrapes you or bruises you (3 and 6), and the nut is used up either way (a fall shakes it loose where it is lost). **Fell the Palm** (drag any axe onto it; 2 ticks) brings down 2 to 4 Green Coconuts and 6 to 10 Palm Fronds and leaves a **Felled Palm**; a shoot rises from the stump after 960 ticks (10 days) as a **Young Palm**, which grows into a Coconut Palm after 2880 ticks (30 days). A Young Palm can be cut instead (any blade or axe) for 2 to 3 Palm Fronds. All tunable.
- **Green Coconut**: drag a knife or axe onto it to **Husk the Coconut** (1 tick): the husk comes apart into 2 of the base game's own Fibers (quality 25 of 47.5, so they spin into yarn like any plant fibre) and leaves a **Husked Coconut**. It spoils in 20 days.
- **Husked Coconut**: drag a hammer, hard hammering stone or axe onto it to **Crack Open** (1 tick): 1 **Coconut Water** (Drink: Thirst +90, a little stomach and calories; spoils in 2 days), 2 **Coconut Meat** (Eat: Satiation +28, stomach +24, calories +70; plateable human food; spoils in 5 days) and 1 **Coconut Shell Bowl**.
- **Coconut Shell Bowl**: a small open container (300) for water that, left on the board in the rain, catches rain the way the base game's Rain Catcher does (50 per rainy tick). Its rain collection has not been watched in a real shower yet.
- **Palm Frond**: weak fuel (tagged tag_Fuel and tag_FuelWeak, fuel 8; tunable). Whether a campfire accepts it as fuel has not been confirmed in game. There is no thatch roof yet, so fuel is its only use.
- **Tropical food plants** (all numbers tunable placeholders): seven wild plants stand on the island boards where Card Survival: Tropical Island places them, each giving a raw food eaten with its own button, and each can be grown in the base game's **Garden Plot**.
  - **Banana Plant** (Vine Woods, Green Deep, Reed Marsh): **Cut a Bunch** (1 tick) gives 3 to 4 **Bananas** (Eat: Satiation +30, stomach +30, calories +40, a little thirst and vitamins; spoils in 3 days).
  - **Mango Tree** (Misty Ridge): **Pick Mangoes** (1 tick) gives 1 to 2 **Mangoes** (Eat: Satiation +26, stomach +30, calories +30, Thirst +12, vitamins +48; spoils in 3 days).
  - **Tropical Almond Tree** (Landfall Strand, Coral Inlet, Palm Fringe, Bleak Strand, Vine Woods, Green Deep, Misty Ridge, Westgrass Plain, Eastgrass Plain, Western Uplands, Eastern Uplands): **Gather Almonds** (1 tick) gives 2 to 3 **Tropical Almonds** (Crack and Eat: Satiation +12, calories +30; keep 30 days).
  - **Wild Jujube** (Eastgrass Plain): **Pick Jujubes** (1 tick) gives 2 to 4 **Jujubes** (Eat: Satiation +14, vitamins +40; spoil in 5 days).
  - **Nipa Palm** (Rootwater Thicket): **Cut a Fruit Head** (1 tick) gives 1 **Nipa Fruit** (Eat the Seeds: Satiation +14, Thirst +10; spoils in 5 days).
  - **Wild Yam Vine** (Vine Woods, Green Deep): **Dig up a Yam** (2 ticks, Filth +3) gives 1 **Yam** (Eat Raw: Satiation +22, stomach +35, calories +30; spoils in 15 days).
  - These six hold 2 or 4 points of yield (bunches, fruit, nuts, tubers), each action spends one and needs one left, and the yield grows back fully over 30 days while you are on that board.
  - **Sago Palm** (Reed Marsh): drag any axe onto it to **Fell the Sago Palm** (3 ticks; needs daylight and stamina 11 or more; the felling costs of the Coconut Palm): 4 to 6 **Sago Pith** (Eat: Satiation +20, stomach +35, calories +35; spoils in 2 days), 2 to 4 Palm Fronds and 1 **Sago Shoot**, and a **Young Sago Palm** that grows back into a Sago Palm after 2880 ticks (30 days).
  - **In a garden**: put a Banana, Mango, Tropical Almond, Jujube, Nipa Fruit, Yam or Sago Shoot into the base game's Garden Plot and on the next tick it takes root as a **growing plant**. A growing plant gains 1 point of Growth a tick from the plot (it does not grow anywhere else) and is ready at 1920 (banana, jujube; 20 days), 2880 (mango, almond, nipa, sago; 30 days) or 1440 (yam; 15 days). **Harvest** (1 tick) gives 3 to 5 Bananas, 2 to 4 Mangoes, 3 to 6 Tropical Almonds, 3 to 5 Jujubes, 1 to 2 Nipa Fruit, 2 to 3 Yams, or 3 to 5 Sago Pith and a new Sago Shoot. Banana, mango, almond, jujube and nipa stay in the plot and grow their next crop over the same time; yam and sago are used up. Each growing or ready plant can be **Uprooted**. The plot's watering, nutrients, acidity and pests do not affect these plants (they read none of the plot's stats), and they grow in every season.
  - Raw only: there are no cooked or prepared island dishes (the plan leaves island cooking out).
- **Medicine and spice plants** (all numbers tunable placeholders): eight more wild plants on the island boards where Card Survival: Tropical Island places them, each with its own pick, cut or dig button (1 tick, or 2 for roots and bark; digging adds Filth +3), holding 2 or 4 points of yield that grow back fully over 30 days while you are on that board, like the food plants.
  - **Ginger Plant** (Rootwater Thicket, Reed Marsh): **Dig up Ginger** gives 1 to 2 **Ginger Root** (Chew: a little food, and Ginger Warmth; spoils in 20 days).
  - **Kava Plant** (Vine Woods, Green Deep): **Dig up Kava Root** gives 1 **Kava Root** (Chew the Root: Kava Calm; spoils in 15 days).
  - **Chilli Plant** (Westgrass Plain, Eastgrass Plain): **Pick Chillies** gives 2 to 4 **Chillies** (Eat: Satiation +3, vitamins +20, Thirst -6; spoil in 7 days).
  - **Lemongrass** (Landfall Strand, Coral Inlet, Palm Fringe, Westgrass Plain, Western Uplands, Hidden Vale): **Cut Lemongrass** gives 2 to 3 **Lemongrass** (Chew: Satiation +2, Thirst +2; spoils in 7 days).
  - **Jasmine Bush** (Green Deep): **Pick Jasmine Flowers** gives 2 to 3 **Jasmine Flowers** (Eat the Petals: Satiation +1; spoil in 3 days; tagged as a flower), and **Take a Jasmine Cutting** gives 1 **Jasmine Cutting** for a garden.
  - **Aloe Vera** (Landfall Strand, Coral Inlet, Palm Fringe, Bleak Strand, Eastgrass Plain, Western Uplands, Eastern Uplands): **Cut an Aloe Leaf** gives 1 **Aloe Vera Leaf** (Apply the Gel: Aloe Salve; spoils in 10 days).
  - **Cinchona Tree** (Vine Woods, Misty Ridge): **Peel Cinchona Bark** gives 1 to 2 **Cinchona Bark** (Chew the Bark: Cinchona Bitters; keeps 60 days), and **Dig up a Cinchona Seedling** gives 1 **Cinchona Seedling** for a garden.
  - **Coffee Bush** (Misty Ridge): **Pick Coffee Berries** gives 3 to 5 **Coffee Berries** (Eat: Satiation +3, vitamins +10, and Coffee Buzz; spoil in 5 days).
  - **Remedies work through effects that wear off**, the way the base game's garlic does: using one raises a Far Territory effect stat that falls by 1 a tick, and while it is up its status changes the base-game stats below. Nothing writes your immune system or your pain directly, so doses do not stack up for the rest of the run.
    - **Aloe Salve** (+24 a leaf, up to 48): Pain -25 and burns heal 1 a tick faster; at 25 or more, Pain -50 and 2 a tick faster.
    - **Cinchona Bitters** (+24 a strip, up to 48): fever falls 10 a tick faster and Pain -25; at 25 or more, 20 a tick faster and Pain -50. It brings a fever down; it does not cure the base game's Swamp Fever infection.
    - **Kava Calm** (+24 a root, up to 48): stress falls 1 a tick faster and Pain -25; at 25 or more, stress 2 a tick faster, Pain -50, and you grow drowsy (wakefulness 0.5 a tick slower).
    - **Coffee Buzz** (+6 a handful, up to 24): wakefulness rises 1.5 a tick faster, stress 0.25 and thirst 0.25 a tick; at 13 or more, double all three.
    - **Ginger Warmth** (+12 a root, up to 24): nausea falls 0.5 a tick faster and Pain -10.
  - **Spices**: Ginger Root (Pungent, medium; Spicy, subtle), Chillies (Spicy, strong), Lemongrass (Herbal, medium; Sour, subtle) and Jasmine Flowers (Floral, strong) carry the base game's flavour tags. Whether a base-game dish picks these flavours up from a Far Territory ingredient has not been checked in game.
  - **In a garden**: put a Ginger Root, Kava Root, Chillies, Lemongrass, Jasmine Cutting, Aloe Vera Leaf, Cinchona Seedling or Coffee Berries into the base game's Garden Plot and it takes root, exactly like the food plants. Ready at 960 (lemongrass; 10 days), 1440 (ginger, chilli; 15 days), 1920 (jasmine, aloe; 20 days) or 2880 (kava, cinchona, coffee; 30 days). **Harvest** gives 2 to 4 Ginger Root, 1 to 2 Kava Root, 3 to 5 Chillies, 3 to 5 Lemongrass, 3 to 5 Jasmine Flowers, 2 to 3 Aloe Vera Leaves, 2 to 3 Cinchona Bark or 4 to 6 Coffee Berries. Ginger and kava are used up; the others stay and grow their next crop.
- **Fibre and flowering plants** (all numbers tunable placeholders): four more wild plants on the island boards where Card Survival: Tropical Island places them, each holding 4 points of yield that grow back fully over 30 days while you are on that board, like the other plants. Each product has a use in the base game or in this mod.
  - **China Rose** (Misty Ridge): **Pick China Roses** (1 tick) gives 2 to 3 **China Rose** flowers (Eat the Petals: Satiation +1; spoil in 3 days; Sour, medium and Floral, subtle; tagged as a human food and a flower), and **Take a China Rose Cutting** (1 tick) gives 1 **China Rose Cutting** for a garden.
  - **Spider Lily** (Reed Marsh): **Cut Spider Lily Leaves** (1 tick) gives 1 to 2 **Spider Lily Leaves** (poisonous, not food; spoil in 5 days), and **Lift a Spider Lily Bulb** (2 ticks, Filth +3) gives 1 **Spider Lily Bulb** for a garden. **Bind as a Poultice** (1 tick) raises a new Far Territory effect stat, **Lily Poultice**, by 24 (up to 24); it falls by 1 a tick, and while it is up, Pain -20. It never writes your pain directly.
  - **Weevil Lily** (Vine Woods, Green Deep): **Cut Weevil Lily Leaves** (1 tick) gives 2 to 3 **Weevil Lily Leaves** (rot in 10 days), and **Dig up a Weevil Lily Rhizome** (2 ticks, Filth +3) gives 1 **Weevil Lily Rhizome** for a garden. **Strip into Fibers** (1 tick) turns a leaf into 1 of the base game's own **Fibers** at quality 25.
  - **Snake Grass** (Vine Woods, Green Deep, Misty Ridge, Rootwater Thicket, Reed Marsh): **Cut Snake Grass** (1 tick) gives 2 to 3 **Snake Grass** blades (rot in 10 days). **Strip into Fibers** (1 tick) turns a blade into 2 of the base game's **Fibers** at quality 35. The Fibers from either plant are the base game's own card, so they make Twine (drag Fibers onto Fibers), feed a fire as tinder and can be combed like any other.
  - **In a garden**: put a China Rose Cutting, Spider Lily Bulb, Weevil Lily Rhizome or Snake Grass blade into the base game's Garden Plot and it takes root, exactly like the other plants. Ready at 960 (snake grass; 10 days), 1440 (spider lily, weevil lily; 15 days) or 1920 (China rose; 20 days). **Harvest** gives 3 to 5 China Roses, 2 to 3 Spider Lily Leaves, 3 to 5 Weevil Lily Leaves or 3 to 5 Snake Grass; all four stay in the plot and grow their next crop.
- **Path and swim cards** (above): each has one action, cannot be trashed, and comes back on arrival if missing.
- **Forage** on Driftwood Shore, Seaward Sands, Landfall Strand, Coral Inlet, the five new shores, Gull Stack and the thirteen interior places: what each place's tideline, scrub or floor holds (see the table above; weights tunable). Each forage spends 96 of a 576-point Gleanings bar that refills by 1 per tick, and needs 15 percent left (tunable).

Foraging and combing need daylight and some stamina, like the base game's own.

## Animals

Five island animals roam their own boards (every number a tunable placeholder). Each is met with the **Approach** button on its card and fought with the base game's combat: a threat, a bite and a chance to get away. **Every bite lands as the base game's own Bite Wound card** and nothing else: there is no venom and no poison stat, and no animal raids your food (the plan leaves CSTI's venom and seagull raids out). What a fight does to you is sized to the animal, from the base game's animals of a similar size (tunable): a seagull, and still more a bat, barely frightens you or spills any blood, and killing one satisfies the Fox Urge as small game does, while the Wolf Urge eases only a little; the macaque and monitor lizard weigh like the base game's fox; the cobra's raised hood frightens you like a wolf's threat, and you feel relief when it slips away.

| Animal | Where | When | Fight (blood) | A kill leaves |
|---|---|---|---|---|
| **Macaque** | Monkey Den, Misty Ridge, Green Deep, Reed Marsh, Vine Woods | 06:00 to 19:00; by night in the Monkey Den | 50 | a **Macaque Carcass** |
| **Monitor Lizard** | Westgrass and Eastgrass Plains, Rootwater Thicket, Bleak Strand, Plain Grotto | 07:00 to 18:00; by night in Plain Grotto | 60 | a **Monitor Lizard Carcass** |
| **Cobra** | Westgrass and Eastgrass Plains, Western and Eastern Uplands | 06:00 to 20:00; gone by night | 30 | a **Cobra Carcass** |
| **Seagull** | flies between Gull Stack, Boulder Shore and Bleak Strand | 05:00 to 20:00; by night on Gull Stack | 20 | the base game's Bird Meat, 2 to 4 Feathers and Bird Bones |
| **Bat** | Bat Roost | always there | 10 | a **Bat Carcass** |

- The **cobra** strikes on its own: once a day, between 08:00 and 18:00, a cobra on the board you stand on starts the fight. The others fight only when you approach them.
- The macaque, monitor lizard and cobra leave tracks as they move (the base game's tracks; a cobra's are faint). A killed animal comes back after a week (the seagull and bat after three days).
- **Carcasses**: drag a knife onto one to **Butcher** it (1 tick) for the base game's raw Meat at quality 72 (macaque 2 to 3 and a Bone, monitor lizard 2 and a Bone, cobra 1, bat 1). A carcass rots into the base game's Rotten Remains in two and a half days (cobra two, bat a day and a half).
- **Seagull Nest** (Gull Stack): **Search the Nest** (1 tick; needs daylight and stamina) gives 1 to 3 of the base game's Wild Eggs with feathers and a twig (weight 4), feathers and twigs only (weight 3), or Bird Bones and a feather (weight 2). A search spends half of its laying, so it can be searched twice from full, and the laying comes back over 10 days.

## Sea dangers

The island's seas bite back (every weight a tunable placeholder, not balanced). The mainland's Seaward Sands stays safe. Each danger is one more weighted outcome of the sea card's own actions, beside the shells, fish and "nothing"; harm lands as the base game's own wound cards, and there is **no venom stat**, no poison and no shark fight (see the last line).

| Danger | Where (Card Survival: Tropical Island's coasts) | Wash in the Sea | Dive | Spear Fish | Harm |
|---|---|---|---|---|---|
| **Shark** | every island sea: Coral Inlet, Rootwater Thicket, Boulder Shore (and Gull Stack), Bleak Strand, Cliffbound Cove | weight 1 | 3 | 6 | the base game's **Bite Wound** |
| **Sea Krait** | Coral Inlet and Rootwater Thicket only | 6 | 6 | 8 | **Bite Wound** |
| **Urchin underfoot** | Coral Inlet, Boulder Shore (and Gull Stack), Bleak Strand, Cliffbound Cove | up to 6 | up to 8 | none | the base game's **Puncture Wound**, and you keep the **Sea Urchin** |

- For scale: a Coral Inlet dive at a full sea weighs shells 30, seaweed 20, conch 10, sea urchin 10, oyster 15 and nothing 30, and a wash weighs nothing 100.
- **Footwear keeps out the urchin.** The urchin outcome's weight follows the sea's Sea Urchin population (`ft_pop_urchin`, as the dive's urchin catch does) and falls by its full weight as the base game's **FootProtection** rises from 0 to 5: the base game's Bark Sandals (5) or Leather Shoes (8) take it out entirely. With FootProtection 5 or more a different outcome takes its place: you tread on the urchin, your footwear takes the spines, and you keep the urchin with no wound. Either way an urchin is one fewer in the sea (1000 off its population).
- **The sea krait** falls by its full weight as FootProtection rises from 0 to 5, and again as **LegProtection** does (no base-game item raises LegProtection; it is read for any mod that does), so the same footwear keeps it off you. On a dive it also falls by its full weight as Swimming rises from 0 to 100.
- **The shark** has no defence but staying out of the island's water. It is not a fight: the framework's animal system starts its encounters from a roaming animal on a board (an Approach button, a duty or a trap), and a sea card's drop has no animal to start one from, so a shark lands as a bite.

## The grassland

The first far land: open grassland well to the west of the forest (every number a tunable placeholder). It has the forest's own weather and seasons: no island warmth.

**Getting there.** Forage in the **Western Wild Woods**, the base game's pine woods at the forest's north-west corner. Each Forage there has a 35% chance (tunable) to turn up a **Trail West**, a game trail that stays on the board (only one at a time). Its **Walk West to Wildrye Plain** takes 6 ticks (an hour and a half; tunable), and on Wildrye Plain a **Trail to the Forest** walks you back the same way. There is no compass button between the forest and the grassland: it is a long walk, so it is a trail card, and the paper map draws no line to it, only the two grassland places once you have visited them.

| Place | Where | What is there |
|---|---|---|
| **Wildrye Plain** | the trail's end, west of the forest | Open grass: two of the base game's Wild Meadowgrass patches, a **Forage** action (fibres and stones) and **Search the Grazing Grounds** (below). Its **West** exit leads to Sedge Fen. |
| **Sedge Fen** | west of Wildrye Plain | Wet ground: the base game's Cattail Patch and Reed Patch and a **Forage** action (fibres and willow twigs). Its **East** exit leads back to Wildrye Plain. |

**The moose.** A **Moose**, this mod's own animal, browses Sedge Fen and grazes Wildrye Plain, whether or not any other mod is installed. It never attacks on its own. Meet it with **Approach**: you may **Leave it alone**, or fight it with the base game's combat (blood 260): it swings its antlers, tries to knock you down, charges and stamps with its hooves, and its wounds are the base game's bruises, sprains, lacerations, fractures and **Puncture Wound** (never a bite). A kill leaves a **Moose Carcass**: drag a knife onto it to **Butcher** it (6 ticks) for 8 to 10 of the base game's raw Meat, 3 Bones, 6 Fresh Skins and, half the time, an Antler. The carcass rots into Rotten Remains in three days, and a killed moose comes back two weeks later. It leaves the base game's tracks as it moves.

**Search the Grazing Grounds** (Wildrye Plain, 3 ticks, needs daylight and stamina) finds the base game's Bones (a 40% chance on half the searches) or nothing.

**With Harsh Wilds installed**, the same search can also turn up what that mod's big grazers leave: a **Wisent Carcass** (12%, any season) and, in winter, a **Musk Ox Carcass** (12%), each on the half of the searches that find remains (tunable). These are Harsh Wilds' own carcass cards, butchered with Harsh Wilds' own actions. **The living wisent and musk ox do not come to the grassland:** they are Harsh Wilds' roaming animals and keep to the forest places Harsh Wilds gives them; only their carcasses turn up here. **Without Harsh Wilds** the search gives only bones or nothing, and nothing is missing or broken: the carcass rows are skipped without a warning.

**With Forest Lore installed**, its **Standing Stones** also stand on Wildrye Plain: the first time you are on the board with Forest Lore loaded, this mod places one ring of them there, once (a board that already holds the stones is left alone). They are Forest Lore's own card with Forest Lore's own actions and nothing of them is copied here. **Without Forest Lore** nothing is placed and nothing is missing or broken: the check finds no such card and does nothing.

## The mountains

The second far land: the snowy heights north of the forest (every number a tunable placeholder).

**Getting there.** Forage in **Fangvale**, the base game's wolf valley at the forest's northern edge. Each Forage there has a 30% chance (tunable) to turn up a **Trail North**, a goat trail that stays on the board (only one at a time). Its **Climb North to Scree Slope** takes 6 ticks (an hour and a half), and on Scree Slope a **Trail to the Forest** walks you back down to Fangvale in 4. As with the grassland there is no compass button between the forest and the mountains, and the paper map draws no line to them, only the mountain places once you have visited them.

| Place | Where | What is there |
|---|---|---|
| **Scree Slope** | the trail's end, north of the forest | Broken stone under grey cliffs: a **Forage** action (stones and flint), the way into the **Greenstone Grotto**, and the cliff up to **Windgap Col**. Its **East** exit leads to Steaming Shelf. |
| **Steaming Shelf** | east of Scree Slope | A **Hot Spring** and a **Rock Salt Seam**, and a **Forage** action (stones). Its **West** exit leads back to Scree Slope. |
| **Windgap Col** | up the cliff above Scree Slope | A snowy saddle: a **Forage** action (stones and flint) and the ways into the **Rust Grotto** and the **Glimmer Grotto**. |
| **Greenstone Grotto** | a cave off Scree Slope, off the paper map | Two of the base game's **Copper Veins** (mine them with a pickaxe for greenstone) and a **Forage** action (stones and now and then a chip of greenstone). |
| **Rust Grotto** | a cave off Windgap Col, off the paper map | A **Forage** action that gathers the base game's **dried bog iron** ore (and stones). |
| **Glimmer Grotto** | a cave off Windgap Col, off the paper map | A **Forage** action for stones and, about one search in nine, the base game's **Geode**, which cracks into a **Calcite Crystal** for the base game's Copper Necklace. |

**Mountain cold.** Scree Slope, Steaming Shelf and Windgap Col lower the temperature you feel by 6 (tunable) while you stand there (no card; the mod adjusts it), so the mountains are always colder than the forest at the same hour; the caves do not have it. Like the island's warmth, this is only what your body feels: the weather and the seasons are still the forest's own, and nothing up here changes the game's base temperature.

**The Hot Spring.** **Bathe in the Hot Spring** (half an hour) washes off 30 filth and leaves you wet (Wetness +60, which on its own makes you feel colder), and gives you **Spring Warmth**, a new effect that adds 16 to the temperature you feel and wears off over 4 hours (all tunable). While you are still wet the net effect is about +6; once you dry, the full +16 holds until the warmth fades. The spring feeds no water channel.

**The Rock Salt Seam.** **Chip out Salt** (half an hour, needs stamina) gives 1 or 2 of the base game's **Salt** and uses a quarter of the seam, which grows back slowly (about one chip a day; tunable). The plan named the base game's Salt Bed for this place; it is not used, because in this game version its Scrape Salt buttons drop no card and its salt only builds up from a liquid poured into it, so on a mountain it would never give anything.

**Rope and Pitons.** A new blueprint in the Advanced Tools tab (unlocked once you have a Rope; 8 ticks to research, 4 to make): a Rope, 2 metal nuggets and a hard hammering tool. Carried in your hand, **Rope and Pitons** raise the base game's **Climbing Aid** by 72 (a quarter of its range; tunable), which makes every climb that reads it safer, the base game's own climbs included. It does not stack into certainty with Advanced Copper Tools' Grappling Hook: both together come to half of Climbing Aid's range.

**The cliff.** **Climb the Cliff to Windgap Col** (half an hour) can only be done with Rope and Pitons **in your hand**; without them the button does not work. Climbing **down** needs no gear. Both climbs can end in a fall (the base game's abrasion or bruise, and you still arrive), are less likely to with climbing skill and Climbing Aid, and always fall when you are tired (Stamina below 16). Each costs stamina, hurts hands and feet a little and trains Climbing.

Every mountain card and place is this mod's own or the base game's. No Advanced Copper Tools card appears here, so the caves are the same with or without it.

## The fjords

The third far land: a cold, steep coast beyond the mountains (every number a tunable placeholder).

**Getting there.** The fjords lie just north of **Windgap Col**: its **North** exit is an ordinary compass hop (a quarter of an hour) to **Fjord Head**, and Fjord Head's **South** exit leads back. Windgap Col itself is up the cliff above Scree Slope, so **reaching the fjords needs Rope and Pitons in your hand** for that one climb (see the mountains above); the way home down the cliff needs no gear. There is no boat to the fjords.

| Place | Where | What is there |
|---|---|---|
| **Fjord Head** | north of Windgap Col | A shingle beach at the head of a long inlet: **The Fjord**, a **Forage** action (stones, kelp, shells) and **Search the Snowfield** (below). Its **East** exit leads to Seal Skerry, its **North** exit to Puffin Cliffs. |
| **Seal Skerry** | east of Fjord Head | A low rock where the seals and the walrus haul out: **The Fjord** and a **Forage** action (kelp, shells, the odd old bone). Its **West** exit leads back to Fjord Head. |
| **Puffin Cliffs** | north of Fjord Head | Burrowed ledges above the water: **The Fjord** and a **Forage** action (stones, feathers and now and then one of the base game's Wild Eggs). Its **South** exit leads back to Fjord Head. |

**Fjord chill.** All three fjord places lower the temperature you feel by 8 (tunable) while you stand there (no card), a little colder than the mountains' 6. As with the mountains and the island, this is only what your body feels: the weather and the seasons are the forest's own.

**The Fjord.** Each place has its own **The Fjord** sea card. **Wash in the Fjord** (a quarter of an hour) washes you as the sea at Seaward Sands does, and leaves you soaked in icy water. **Watch the Water** (half an hour) is a look out over the fjord: now and then a **whale** surfaces far out (weight 1), more often seals bob up and watch you (weight 2), and most of the time there is only grey water (weight 4). The whales are a sight, not an animal: there is nothing to hunt from the shore, and no boat to hunt from. There is no salt to boil from the fjord's water: the plan leaves sea salt out, and salt comes from the mountains' Rock Salt Seam and the base game's own sources.

**Animals.** Three of this mod's own animals live here (framework animal system; every number a tunable placeholder). Each is met with **Approach** and fought with the base game's combat: a threat, a bite and a chance to get away, and **every bite lands as the base game's Bite Wound**. None attacks on its own, and none leaves tracks. What a fight does to you is sized to the animal (tunable): a puffin barely touches your fear or spills any blood and counts as small game, like the bat; a seal weighs like the base game's fox with a bigger kill; a walrus fight weighs like the base game's boar, with a bear's roar.

| Animal | Where | When | Fight (blood) | A kill leaves |
|---|---|---|---|---|
| **Seal** | Seal Skerry and Fjord Head | always; it mostly slips back into the water | 80 | a **Seal Carcass** |
| **Walrus** | Seal Skerry | always | 220 (a hard, dangerous fight) | a **Walrus Carcass** |
| **Puffin** | Puffin Cliffs, Fjord Head and Seal Skerry | 05:00 to 20:00; by night on Puffin Cliffs | 8 | the base game's Bird Meat, 1 to 2 Feathers and Bird Bones |

- **Seal Carcass**: drag a knife onto it to **Butcher** it (3 ticks) for 3 to 4 of the base game's raw Meat, 1 to 2 Fat Chunks, 2 Fresh Skins and 1 to 2 Bones. It rots into Rotten Remains in two and a half days. A killed seal comes back after a week.
- **Walrus Carcass**: **Butcher** (6 ticks) for 8 to 10 raw Meat, 3 to 4 Fat Chunks, 6 Fresh Skins, 3 Bones and 2 of the base game's **Tusks**. It rots in three days. A killed walrus comes back after two weeks, a puffin after three days.

**Search the Snowfield** (Fjord Head, 3 ticks, needs daylight and stamina) finds the base game's Bones (a 40% chance on half the searches) or nothing. **With Harsh Wilds installed** it can also turn up a **Musk Ox Carcass** (15% on the half of the searches that find remains, in winter only, as on the grassland; tunable): Harsh Wilds' own carcass card, butchered with Harsh Wilds' own action. The living musk ox does not come here; it is Harsh Wilds' roaming animal and keeps to the places Harsh Wilds gives it. **Without Harsh Wilds** the search gives only bones or nothing, and the row is skipped without a warning.

## Notes

- The fjords need no new save: their three places get their save entries when a run starts. They are reached through Windgap Col, so a save that has never climbed the cliff reaches them the same way. The seal, walrus and puffins are not board cards and are expected to appear in an existing save too (not yet confirmed in game).
- The mountains need no new save: their six places get their save entries when a run starts, and the Trail North is a Forage find on Fangvale's own action, so an older save finds it on its next forage there.
- The grassland needs no new save. Its two places get their save entries when a run starts, and the Trail West is a Forage find on the Western Wild Woods' own action, so an older save finds it on its next forage there. The moose is not a board card and is expected to appear in an existing save too (not yet confirmed in game).
- The Seagull Nest is one of Gull Stack's first cards: a save that has already visited Gull Stack does not gain it. The animals are not board cards and are expected to appear in an existing save too (not yet confirmed in game).
- The tropical food plants, the medicine and spice plants and the fibre and flowering plants follow the palms' rule: they are among a board's first cards, so a save that already visited a board does not gain them there, and they are not put back if lost. Start a new run to see them. The Garden Plot growing needs no new save.
- Palms only appear on places whose save entry was created by this version: a save that already visited Landfall Strand, Coral Inlet, Boulder Shore, Palm Fringe or Gull Stack has no palms there. Start a new run to see them. Palms are not put back if lost.
- The new beaches get their cards the first time a save is loaded with the mod, so cards added to them in a later version of the mod do not appear on a save that already has them. The exception is a card that can only be there once, such as the Outrigger Canoe: Seaward Sands and Landfall Strand put those back on arrival (and on loading a save made on either beach), which is how a save from the coast-only build gains the crossing, and how a save made while the sails were still on the two old boats gains the canoe on each beach.
- Sea fishing needs no new save: the fishing, spearing and trap actions live on the sea cards' data, so a save that already has its sea cards gains them, and the population stats start full the first time they are read. The Fish Trap blueprint is an ordinary blueprint, researchable from the start.
- The Sea and the Tide Pool follow the same first-visit rule and are not among those put back: a save that has already visited Seaward Sands does not gain them. Start a new run, or visit Seaward Sands for the first time with this version, to see them.
- The Reef Water is among the cards put back, so a save that has already landed on Landfall Strand gains it on the next landing. The old Island Air, Mountain Air, Fjord Chill and Volcanic Heat cards no longer exist: an existing save simply loses them, and the climate is applied from where you stand.
- Each new place adds a small entry to the save, about 5 KB each (measured on a day-1 save), created when a run starts.
- **A long stay on the island leaves home behind.** When you come back to a place you left, the game catches up on the time that passed there, but the framework caps that catch-up at 14 in-game days (1,344 ticks). After a longer island stay your home base is caught up for 14 days only: food spoils, fires burn down and crops grow as if you had been away 14 days.
- The six new interior places get their save entries when a run starts, so their cards are there on the first visit. The new path cards on Bleak Strand, Reed Marsh and Hidden Vale are among the cards put back on arrival, and the new compass buttons on Westgrass Plain, Eastgrass Plain and Misty Ridge are on the location cards and need no save change, so an older save gains both on its next visit.
- The interior's new path cards on Palm Fringe, Cliffbound Cove and the new boards are put back on arrival the same way, and the new compass buttons on Coral Inlet, Rootwater Thicket and Bleak Strand are on the location card and need no save change.
- The new path cards on Landfall Strand and Coral Inlet are among the cards put back on arrival, so a save that has already visited those beaches gains them on its next visit.
- The sixteen caves get their save entries when a run starts, like the interior. Their entrance path cards on the shores and hills, and the new ways on from the Black Grotto, Sinkhole, Lava Tube and Cliffbound Cove, are path cards of the kind put back on arrival, so an older save is expected to gain them on its next visit to that place (not yet watched in a real older save). The Copper Veins and the Spring are not put back: they appear in a hall whose entry this version creates, and a mined-out vein does not return.
