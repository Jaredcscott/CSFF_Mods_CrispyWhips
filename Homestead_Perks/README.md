# Homestead Perks

**Version:** 1.3.4
**Author:** Jared
**Requires:** CSFFModFramework (soft dependency)
**Language:** English, Simplified Chinese

Forty-four character-creation perks for Card Survival: Fantasy Forest. Most hand you a kit you carry and Place once, so a cabin, forge or mine stands from day one with no research or building. The perks appear in the Situational tab.

## What you get

| Group | Perks | Count |
|---|---|---|
| Bundle | Homestead | 1 |
| Placeable kits | Cabin, Mud Hut, Cellar, Well, Forge, Furnace, Oven, Pit Trap, Log Bed, Tanning Pit, Rain Cistern, Kiln, Drying Rack, Bloomery, Enclosure, Copper/Flint/Witchstone Mine | 18 |
| Station kits | Stretching Frame, Charcoal Mound, Wall Shelf, Snow Shelter, Compost Bin, Weighted Loom, Ritual Pyre, Frost Shrine, Garden Plot, Deadfall Trap, Log Boom, Tilled Field, Levee, Mushroom Bed | 14 |
| Road charges | Path Kit | 1 |
| Vanilla pieces, already made | Rustic Table Kit, Bookshelf Kit, Sledge, Travois, Weaver's Workshop Kit, Sawhorses Kit, Pottery Wheel Kit, Fleshing Beam Kit, Workbench Kit, Chest Kit | 10 |

The mod has no settings and no console commands.

**Install:** extract into `BepInEx/plugins/`, or use Mod Update Manager. CSFF Mod Framework 2.28.0 or later is needed for the Homestead Kit's materials and nails to be correct.

**Compatibility:** needs no other mod. Community Mod Chest has two overlapping perks (see Notes).

**Credits:** nine perks are extracted from Community Mod Chest's Homestead perks, its recreation of the abandoned "Better Perk Buildings" mod. Thanks to Chiwei, whose report led to the 1.3.1 starting-material fixes.

## The perks

Costs are in Suns. Weights are what the item weighs once you pick it up.

- **Homestead** (100). A Homestead Kit that unpacks into a Cabin Kit, two Rain Cistern Kits and building materials: 30 Wooden Planks, 30 Mud Bricks, 50 Stones, 20 Heavy Stones, 15 Tree Logs, 20 Clay, 10 Rope, 20 Metal Nuggets and 50 Metal Nails. Advanced Copper Tools is not needed.
- **Cabin Kit** (60) and **Mud Hut Kit** (40). Each also grants a broom blueprint. Neither can be placed indoors or in a cave.
- **Cellar Kit** (40). An underground room where stored food lasts longer. It cannot be placed in an animal enclosure, indoors, in a cave, beside a river or in wetlands.
- **Well Kit** (35). A permanent water source. Not in an animal enclosure.
- **Forge Kit** (25), **Furnace Kit** (20), **Oven Kit** (15), **Rain Cistern Kit** (10).
- **Log Bed Kit** (15). Cannot be placed while a Log Bed already stands on the board.
- **Tanning Pit Kit** (15). Not in an animal enclosure.
- **Pit Trap Kit** (20). A big-game trap that still needs bait. Its placement is not restricted.
- **Kiln Kit** (15, weight 600). A vanilla Kiln for firing pottery and extracting tar and quicklime. Vanilla draws it with the Oven's art, so the kit does too.
- **Drying Rack Kit** (10, weight 250).
- **Bloomery Kit** (30, weight 900). A vanilla Bloomery for smelting iron. It still needs charcoal, plus bellows or a windy spot.
- **Enclosure Kit** (25, weight 800). An animal Enclosure entrance whose Enter opens the pen, a home for vanilla's Starting Poultry and Starting Cattle. Not in an animal enclosure.
- **Copper Mine Kit**, **Flint Mine Kit** and **Witchstone Mine Kit** (40 each, weight 800). Place one at a deposit of its ore to open vanilla's mine entrance, skipping the 12-stage build and 48 hours of research.
  - Away from a deposit, a "Needs a Deposit" action says why. Once an entrance stands there, a second kit shows "Already Have a Mine Here".
  - Advanced Copper Tools' and Forest Lore's caves also count as flint deposits.
- **Path Kit** (20). You start with 6 Path Kits. Each has four "Build Path" actions (North, South, East, West) that instantly finish the vanilla road in that direction from where you stand.
  - Indoors, a "Must be Outdoors" action replaces the four buttons.
  - A direction with no route, or one already built, does nothing and spends no kit. It shows no message.
- **Stretching Frame Kit**, **Charcoal Mound Kit**, **Wall Shelf Kit**, **Snow Shelter Kit**, **Compost Bin Kit** (15 each) and **Weighted Loom Kit** (30). Place the kit to get that vanilla structure. They weigh 300, 300, 250, 200, 400 and 700.
  - The Charcoal Mound is placed already burning and spends no wood.
  - The Weighted Loom is the upright loom, not the treadle loom. The Wall Shelf is a plain shelf, not the bookshelf.
  - Snow Shelter: "Already Have One" while one stands. Compost Bin: "Not in a Pen" inside an animal enclosure.
  - Not checked: the Wall Shelf's sheltered-room requirement, the Snow Shelter's snow, and the light and stamina vanilla asks of every build.
- **Ritual Pyre Kit** and **Frost Shrine Kit** (15 each, weight 1000). Place the kit to get the shrine without research or building. Only one of each can stand on a board ("Already Have One"). The Frost Shrine places only in freezing cold (base temperature -20 or below), as vanilla requires.
- **Garden Plot Kit**, **Deadfall Trap Kit**, **Log Boom Kit** (15 each), **Tilled Field Kit** (30), **Levee Kit** (40) and **Mushroom Bed Kit** (15). They weigh 300, 300, 700, 600, 500 and 300.
  - Garden Plot, Tilled Field and Mushroom Bed: "Not in a Pen" inside an animal enclosure. Deadfall Trap: "Not Indoors". Log Boom and Levee: "Needs a River".
  - Vanilla's Garden Plot and Tilled Field also check a tag no card in the game carries, so that check is not copied.
  - Vanilla gives no way to build the Mushroom Bed and nothing plants mushrooms in it, so it may do little beyond standing there. Its kit uses the Garden Plot Kit's icon.
  - Not checked: light, stamina and research requirements.
- **Rustic Table Kit** (25, weighs 2250). Vanilla's Dismantled Rustic Table and Rustic Stool. Build the table and Place the stool. Both raise your Sanctuary, and the table stores your things. Vanilla gives no way to build the stool.
- **Bookshelf Kit** (15, weight 1500). Vanilla's Dismantled BookShelf, which vanilla gives no way to build. Placed, it stores your things, keeps food fresh longer and raises your Sanctuary.
- **Chest Kit** (15, weight 3000). Vanilla's Storage Chest, which vanilla gives no way to build. Set it down before a long trip. It uses the Bookshelf Kit's shelf icon.
- **Sledge** (20). A finished Sledge at full durability, for hauling over snow or riding downhill.
- **Travois** (15, weight 300). A finished Travois at full durability: a carrying frame that costs some speed.
- **Weaver's Workshop Kit** (35). A Treadle Loom, Spinning Wheel and Fiber Break. Place each wherever you like.
- **Sawhorses Kit**, **Pottery Wheel Kit** and **Fleshing Beam Kit** (15 each, weight 1000). That station, already made.
- **Workbench Kit** (20, weight 1000). A vanilla Workbench, already made. Build it somewhere sheltered and lit, as vanilla requires. A Farmer's open River Clearing start is neither.

Vanilla's stool and bookshelf have no card art of their own. This mod gives both a picture, carried and placed. The Rustic Table and the Sledge can be researched in vanilla; the bookshelf, stool and chest have no unlock at all.

## Placing kits

Every structure kit is carried anywhere and raised once with a single "Place" action. Where a kit cannot be placed, an action names the reason instead:
- "Not in a Pen": inside an animal enclosure.
- "Already Have One": a Log Bed, Snow Shelter, Ritual Pyre or Frost Shrine already stands.
- "Not Indoors": the Cabin, Mud Hut and Cellar kits indoors or in a cave.
- "Not by the River", "Not in Wetlands" or "Not on Wet Ground": the Cellar Kit, as vanilla's own blueprint refuses.

A few environments mark a river or wetland only on the environment itself. There the Cellar Kit is refused with no explanatory action. Vanilla's own build rules for the Well and the Pit Trap are empty in game, so those are not checked.

## Notes

Nine perks (Homestead, Cabin, Mud Hut, Log Bed, Furnace, Forge, Oven, Rain Cistern, Tanning Pit) are a trimmed extraction of Community Mod Chest's Homestead perks. None of its village content, NPCs or quests is included. The other thirty-five perks are new.

Community Mod Chest now keeps two kit perks that overlap with this mod: its "Founders Kit" is the same bundle as Homestead, and its "Extra Rain Cistern Kit" matches the Rain Cistern Kit. Running both is safe but redundant, and you will see both at character creation.

### Weight

Perks whose starting items weigh 800 or more say so in their description. Starting items land on the ground, so they weigh nothing until you pick them up. In play, three heavy perks together began at Encumbrance 0.

Carried items count toward the 4000 Encumbrance cap. At the cap, vanilla blocks travel, Forage, Swim and Climb ("Too heavy to move..."). Drop, pick-up and Place still work: a player carrying 4950 could drop a piece, pick it up and Place the Cabin Kit, and travel returned once the load fell to 3750. Place heavy pieces before a trip.

## Version history

Full list in CHANGELOG.md.
- 1.3.4: card pictures framed like vanilla's again (1.3.2's crop stretched them over the whole card).
- 1.3.3: the Rustic Stool and the Bookshelf have pictures (the game ships both blank).
- 1.3.2: card art cropped to the subject, like vanilla's.
- 1.3.1: Homestead Kit materials made usable; 28 new perks and kits; placement guards.
- 1.3.0: Rustic Table Kit, Bookshelf Kit and Sledge.
