# Homestead Perks

**Version:** 1.3.10
**Author:** Jared
**Requires:** CSFFModFramework (soft dependency)
**Language:** English, Simplified Chinese

Forty-five character-creation perks for Card Survival: Fantasy Forest. Most hand you a kit you carry and Place once, so a cabin, forge or mine stands from day one with no research or building. The perks appear in the Situational tab.

## What you get

| Group | Perks | Count |
|---|---|---|
| Bundles | Homestead, Smallholding | 2 |
| Placeable kits | Cabin, Mud Hut, Cellar, Well, Forge, Furnace, Oven, Pit Trap, Log Bed, Tanning Pit, Rain Cistern, Kiln, Drying Rack, Bloomery, Enclosure, Copper/Flint/Witchstone Mine | 18 |
| Station kits | Stretching Frame, Charcoal Mound, Wall Shelf, Snow Shelter, Compost Bin, Weighted Loom, Ritual Pyre, Frost Shrine, Garden Plot, Deadfall Trap, Log Boom, Tilled Field, Levee, Mushroom Bed | 14 |
| Road charges | Path Kit | 1 |
| Vanilla pieces, already made | Rustic Table Kit, Bookshelf Kit, Sledge, Travois, Weaver's Workshop Kit, Sawhorses Kit, Pottery Wheel Kit, Fleshing Beam Kit, Workbench Kit, Chest Kit | 10 |

The mod has no settings and no console commands.

**Install:** extract into `BepInEx/plugins/`, or use Mod Update Manager. CSFF Mod Framework 2.29.0 or later is needed: it unpacks the Homestead Kit (on an older framework the kit is used up and gives nothing).

**Compatibility:** needs no other mod. Community Mod Chest has two overlapping perks (see Notes).

**Credits:** nine perks are extracted from Community Mod Chest's Homestead perks, its recreation of the abandoned "Better Perk Buildings" mod. Thanks to Chiwei, whose report led to the 1.3.1 starting-material fixes.

## The perks

Costs are in Suns unless a Moon or a Star is named. Weights are what the item weighs once you pick it up.

- **Homestead** (1 Moon and 1 Star). A Homestead Kit that unpacks into a Cabin Kit, two Rain Cistern Kits and building materials: 15 Wooden Planks, 15 Mud Bricks, 20 Stones, 6 Heavy Stones, 6 Tree Logs, 10 Clay, 5 Rope, 9 Metal Nuggets and 20 Metal Nails. Advanced Copper Tools is not needed.
- **Cabin Kit** (1 Star) and **Mud Hut Kit** (1 Moon). Each also grants a broom blueprint. Neither can be placed indoors or in a cave.
- **Cellar Kit** (1 Moon). An underground room where stored food lasts longer. It cannot be placed in an animal enclosure, indoors, in a cave, beside a river or in wetlands.
- **Well Kit** (1 Moon). A permanent water source. Not in an animal enclosure.
- **Well blueprint** (no perk needed). Vanilla's own Well blueprint was never in the journal, so no one could build a Well. With a shovel on the board it can now be researched and is built from Farming > Agriculture at vanilla's own cost (eleven stages). The Well Kit above still places a Well at once.
- **Forge Kit** (35), **Furnace Kit** (20), **Oven Kit** (15), **Rain Cistern Kit** (20).
- **Log Bed Kit** (15). Cannot be placed while a Log Bed already stands on the board.
- **Tanning Pit Kit** (15). Not in an animal enclosure.
- **Pit Trap Kit** (20). A big-game trap that still needs bait. Its placement is not restricted.
- **Kiln Kit** (15, weight 600). A vanilla Kiln for firing pottery and extracting tar and quicklime. Vanilla draws it with the Oven's art, so the kit does too.
- **Drying Rack Kit** (15, weight 250).
- **Bloomery Kit** (40, weight 900). A vanilla Bloomery for smelting iron. It still needs charcoal, plus bellows or a windy spot.
- **Enclosure Kit** (25, weight 800). An animal Enclosure entrance whose Enter opens the pen, a home for vanilla's Starting Poultry and Starting Cattle. Not in an animal enclosure.
- **Smallholding** (1 Moon). An Enclosure Kit plus vanilla's Nesting Box, both placed with Place: a pen and a nest for the animals vanilla's Starting Poultry or Starting Cattle (1 Moon each) bring. Starting Poultry already gives one Nesting Box, so taken together you have two.
- **Copper Mine Kit**, **Flint Mine Kit** and **Witchstone Mine Kit** (1 Moon each, weight 800). Place one at a deposit of its ore to open vanilla's mine entrance, skipping the 12-stage build and 48 hours of research.
  - Away from a deposit, a "Needs a Deposit" action says why. Once an entrance stands there, a second kit shows "Already Have a Mine Here".
  - Advanced Copper Tools' and Forest Lore's caves also count as flint deposits.
- **Path Kit** (20). You start with 3 Path Kits. Each has four "Build Path" actions (North, South, East, West) that instantly finish the vanilla road in that direction from where you stand.
  - Indoors, a "Must be Outdoors" action replaces the four buttons.
  - A direction with no route, or one already built, does nothing and spends no kit. It shows no message.
- **Stretching Frame Kit**, **Charcoal Mound Kit**, **Wall Shelf Kit**, **Compost Bin Kit** (15 each), **Snow Shelter Kit** (20) and **Weighted Loom Kit** (30). Place the kit to get that vanilla structure. They weigh 300, 300, 250, 400, 200 and 700.
  - The Charcoal Mound is placed already burning and spends no wood.
  - The Weighted Loom is the upright loom, not the treadle loom. The Wall Shelf is a plain shelf, not the bookshelf.
  - Snow Shelter: "Already Have One" while one stands. It places only where snow lies on the ground, the "Snow Needed" vanilla asks before you can research its blueprint. With no snow, Place is greyed out with a notice. Compost Bin: "Not in a Pen" inside an animal enclosure.
  - Not checked: the Wall Shelf's sheltered-room requirement, and the light and stamina vanilla asks of every build.
- **Ritual Pyre Kit** and **Frost Shrine Kit** (15 each, weight 1000). Place the kit to get the shrine without research or building. Only one of each can stand on a board ("Already Have One"). The Frost Shrine places only in freezing cold (base temperature -20 or below), as vanilla requires.
- **Garden Plot Kit**, **Deadfall Trap Kit**, **Log Boom Kit** (15 each), **Tilled Field Kit** (30), **Levee Kit** (40) and **Mushroom Bed Kit** (15). They weigh 300, 300, 700, 600, 500 and 300.
  - Garden Plot, Tilled Field and Mushroom Bed: "Not in a Pen" inside an animal enclosure. Deadfall Trap: "Not Indoors". Log Boom and Levee: "Needs a River".
  - Vanilla's Garden Plot and Tilled Field also check a tag no card in the game carries, so that check is not copied.
  - Vanilla gives no way to build the Mushroom Bed, but the bed itself works: put fresh Forest Caps, Fly Agaric or Spirit Mushrooms in its six slots and they take root and grow, twice as fast as in a Garden Plot (faster still in autumn). Its kit uses the Garden Plot Kit's icon.
  - Not checked: light, stamina and research requirements.
- **Rustic Table Kit** (25, weighs 2250). Vanilla's Dismantled Rustic Table and Rustic Stool. Build the table and Place the stool. Both raise your Sanctuary, and the table stores your things. Vanilla gives no way to build the stool.
- **Bookshelf Kit** (15, weight 1500). Vanilla's Dismantled BookShelf, which vanilla gives no way to build. Placed, it stores your things, keeps food fresh longer and raises your Sanctuary.
- **Chest Kit** (15, weight 3000). Vanilla's Storage Chest, which vanilla gives no way to build. Set it down before a long trip. It uses the Bookshelf Kit's shelf icon.
- **Sledge** (20). A finished Sledge at full durability, for hauling over snow or riding downhill.
- **Travois** (15, weight 300). A finished Travois at full durability: a carrying frame that costs some speed.
- **Weaver's Workshop Kit** (50). A Treadle Loom, Spinning Wheel and Fiber Break. Place each wherever you like.
- **Sawhorses Kit** and **Fleshing Beam Kit** (15 each) and **Pottery Wheel Kit** (25), each weight 1000. That station, already made.
- **Workbench Kit** (20, weight 1000). A vanilla Workbench, already made. Build it somewhere sheltered and lit, as vanilla requires. A Farmer's open River Clearing start is neither.

Moons and Stars are vanilla's rarer perk currencies: a Moon comes on in-run days 30, 60 and 90, a Star on days 120 and 240. Vanilla prices its own Cabin Start at 1 Star and Mudhut Start at 1 Moon, and since 1.3.5 this mod prices its houses, cellar, well and mines the same way. A perk you already bought stays yours.

Vanilla's stool and bookshelf have no card art of their own. This mod gives both a picture, carried and placed. The Rustic Table and the Sledge can be researched in vanilla; the bookshelf, stool and chest have no unlock at all.

## Placing kits

Every structure kit is carried anywhere and raised once with a single "Place" action. Where a kit cannot be placed, an action names the reason instead:
- "Not in a Pen": inside an animal enclosure.
- "Already Have One": a Log Bed, Snow Shelter, Ritual Pyre or Frost Shrine already stands.
- "Not Indoors": the Cabin, Mud Hut, Cellar and Deadfall Trap kits indoors or in a cave.
- "Needs a River": the Log Boom and Levee kits away from a river.
- "Needs a Deposit" or "Already Have a Mine Here": a mine kit away from its ore, or where its mine already stands.
- "Not by the River", "Not in Wetlands" or "Not on Wet Ground": the Cellar Kit, as vanilla's own blueprint refuses.

A few environments mark a river or wetland only on the environment itself. There the Cellar Kit is refused with no explanatory action. The Well Kit checks only vanilla's enclosure rule, and the Pit Trap Kit nothing, since vanilla's Pit Trap blueprint names no card; neither kit checks vanilla's shelter rule.

## Notes

Nine perks (Homestead, Cabin, Mud Hut, Log Bed, Furnace, Forge, Oven, Rain Cistern, Tanning Pit) are a trimmed extraction of Community Mod Chest's Homestead perks. None of its village content, NPCs or quests is included. The other thirty-six perks are new.

Community Mod Chest now keeps two kit perks that overlap with this mod: its "Founders Kit" is the same bundle as Homestead, and its "Extra Rain Cistern Kit" matches the Rain Cistern Kit. Running both is safe but redundant, and you will see both at character creation.

### Weight

Perks whose starting items weigh 800 or more say so in their description. Starting items land on the ground, so they weigh nothing until you pick them up. In play, three heavy perks together began at Encumbrance 0.

Carried items count toward the 4000 Encumbrance cap. At the cap, vanilla blocks travel, Forage, Swim and Climb ("Too heavy to move..."). Drop, pick-up and Place still work: a player carrying 4950 could drop a piece, pick it up and Place the Cabin Kit, and travel returned once the load fell to 3750. Place heavy pieces before a trip.

## Version history

Full list in CHANGELOG.md.

- 1.3.9: the mod list description says the Homestead's 20 metal nails need CSFF Mod Framework 2.28.0 or later.
- 1.3.10: the framework unpacks the Homestead Kit (same contents); needs CSFF Mod Framework 2.29.0 or later.
- 1.3.8: a game update can no longer lock the Path Kit: when the mod cannot read a location's road list, Build Path goes ahead and the log says why.
- 1.3.7: the Well blueprint's Build works (vanilla's blueprint carried a condition no board could meet).
- 1.3.6: vanilla's Well blueprint can be researched and built; new Smallholding perk; the Snow Shelter Kit places only where snow lies.
- 1.3.5: vanilla balance pass: structure starts cost Moons and Stars, a smaller Homestead stockpile, 3 Path Kits, several higher Sun prices; the Mushroom Bed's growing is documented.
- 1.3.4: card pictures framed like vanilla's again (1.3.2's crop stretched them over the whole card).
- 1.3.3: the Rustic Stool and the Bookshelf have pictures (the game ships both blank).
- 1.3.2: card art cropped to the subject, like vanilla's.
- 1.3.1: Homestead Kit materials made usable; 28 new perks and kits; placement guards.
- 1.3.0: Rustic Table Kit, Bookshelf Kit and Sledge.
