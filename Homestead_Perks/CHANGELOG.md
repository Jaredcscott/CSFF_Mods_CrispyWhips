# Changelog

## 1.3.5 (2026-10-15)

A balance pass so the perks fit the vanilla game instead of skipping it.

- **Built structures now cost Moons and Stars, as vanilla prices its own.** Vanilla sells its Cabin Start for 1 Star and its Mudhut Start for 1 Moon. Cabin Kit is now 1 Star (was 60 Suns). Mud Hut, Cellar, Well and the Copper, Flint and Witchstone Mine Kits are now 1 Moon each (were 35 to 40 Suns). A perk you have already bought stays yours.
- **Homestead is now 1 Moon and 1 Star (was 100 Suns), and its stockpile is smaller:** 15 Planks, 15 Mud Bricks, 20 Stones, 6 Heavy Stones, 6 Tree Logs, 10 Clay, 5 Rope, 9 Metal Nuggets (vanilla Smithing Gear's count) and 20 Metal Nails. The Cabin Kit and two Rain Cistern Kits are unchanged. If you already own the perk, your next run unpacks the new stockpile; a Homestead Kit already unpacked in a save keeps its pile.
- **Path Kit now gives 3 kits** (was 6), still 20 Suns. Kits already in a save stay.
- **Higher Sun prices:** Weaver's Workshop Kit 50 (was 35), Bloomery Kit 40 (was 30), Forge Kit 35 (was 25), Pottery Wheel Kit 25 (was 15), Rain Cistern Kit 20 (was 10), Snow Shelter Kit 20 (was 15), Drying Rack Kit 15 (was 10).
- **The Mushroom Bed grows mushrooms.** The description used to say nothing plants mushrooms in it. That was wrong: the vanilla bed has six slots and its own growing recipes. Put fresh Forest Caps, Fly Agaric or Spirit Mushrooms in it and they take root and grow, twice as fast as in a Garden Plot. The perk and kit text now say so.

## 1.3.4 (2026-10-14)

- **Card pictures sit where they belong again.** In the previous update every item picture was stretched to fill its whole card, sitting high with the name banner over it. The pictures are back in the game's own framing: centred, low on the card, with space above, like the vanilla cards beside them. Thanks to CHIWEI for the report.

## 1.3.3 (2026-10-14)

- **The Rustic Stool and the Bookshelf have pictures.** The game ships both with no card art at all, so the stool from the Rustic Table Kit perk and the bookshelf from the Bookshelf Kit perk were blank cards, carried or placed. They now show paintings shipped with this mod.

## 1.3.2 (2026-10-12)

- **Well Kit has a picture again on game EA 0.69c:** the update removed the vanilla Well picture that the Well Kit card and its perk icon borrowed, so both were blank. They now use a stone well painting shipped with this mod.
- Card art in the vanilla form: the card picture is now cropped to the subject and saved as small palette images, the way the game's own art is, so each item fills its card like a vanilla item does and the pictures take about a sixth of the space they did.
- Cleaner art: white left by the background removal is gone from the Homestead perk and Homestead Kit picture. No card data changed.

## 1.3.1 (2026-09-28)

Thanks to Chiwei, whose report is behind this release.

- **Homestead Kit materials now work.** Its 30 Planks are oak (they had no wood type, so no recipe took them), its 15 Tree Logs are oak logs as a felled Large Oak gives them, its Clay is quality 30, and its 20 Metal Nuggets are cool copper at quality 50. Needs CSFF Mod Framework 2.28.0.
- **Homestead Kit also unpacks 50 Copper Nails**, whether or not Advanced Copper Tools is installed. On an older framework the nails are skipped with no error.
- **28 new perks** in the Situational tab:
  - Travois (15 Suns), Kiln Kit (15), Drying Rack Kit (10) and Bloomery Kit (30).
  - Weaver's Workshop Kit (35), Sawhorses Kit, Pottery Wheel Kit and Fleshing Beam Kit (15 each) and Workbench Kit (20).
  - Enclosure Kit (25) and Chest Kit (15).
  - Copper, Flint and Witchstone Mine Kits (40 each): a mine entrance only where that ore's deposit is on the board.
  - Fourteen station kits: Stretching Frame, Charcoal Mound, Wall Shelf, Snow Shelter, Compost Bin, Weighted Loom, Ritual Pyre, Frost Shrine, Garden Plot, Deadfall Trap, Log Boom, Tilled Field, Levee and Mushroom Bed. Prices are listed in the README.
- **Placement limits:** the Cellar, Well and Tanning Pit kits no longer Place inside an animal enclosure ("Not in a Pen"). The Log Bed Kit no longer Places while a Log Bed stands ("Already Have One"). The Cabin and Mud Hut kits no longer Place indoors or in a cave, and the Cellar Kit also refuses river, wetland and bog ground. The new station and mine kits carry their own limits, listed in the README.
- **Path Kit:** indoors, a "Must be Outdoors" action replaces the four Build Path buttons. A press toward a direction with no route, or one already built, now does nothing and keeps your kit. Before, it spent a kit and made a road that led nowhere.
- **Weights in the perk text.** The Cabin, Mud Hut, Cellar, Forge, Log Bed and Path Kit descriptions now say what they weigh. Starting items land on the ground, so they weigh nothing until picked up. At 4000 Encumbrance vanilla blocks travel, Forage, Swim and Climb, while Drop, pick-up and Place still work. An earlier note here said you could still travel over the cap; that was wrong.
- **Wording fixed:** vanilla gives no way to build the bookshelf or the rustic stool, so the Rustic Table Kit and Bookshelf Kit no longer claim to skip research and building for them. The table and Sledge keep that wording.
- **Description fixed:** the mod description grouped Path Kit with the one-time Place kits. It starts you with 6 charges and has no Place action.
- Cleaner perk and card text, English and Chinese.

## 1.3.0 (2026-09-28)

- **Three new perks** in the Situational tab, each skipping vanilla's research and building:
  - Rustic Table Kit (25 Suns): vanilla's Dismantled Rustic Table and a Rustic Stool. Both raise your Sanctuary, and the table stores your things.
  - Bookshelf Kit (15 Suns): vanilla's Dismantled BookShelf. Placed, it stores your things, keeps food fresh longer and raises your Sanctuary.
  - Sledge (20 Suns): a finished vanilla Sledge at full durability.
- These are vanilla's own carried pieces, so the placed results behave exactly as vanilla's do. The table and bookshelf are heavy (1500 each, 2250 for the table and stool together), so set them down before a long trip.
- Vanilla's stool and bookshelf have no card art, so these show none either.
- No existing perk, kit or save changes.

## 1.2.4 (2026-09-18)
- Perks now carry the "[HSP]" tag that CSFF Mod Framework 2.26.0 shows after a mod perk's name.
  `ModInfo.json` gains `"ShortName": "HSP"`. Without it the framework falls back to the initials of
  the mod's name, so all 13 Homestead perks would have read "[HP]". Display only: perk names, saves
  and effects are unchanged, and the tag can be hidden with the framework's `ShowModOriginTag`
  setting.

## 1.2.3 (2026-08-16)
- `ModInfo.json` Description corrected: the Path Kit blurb still said "3 kits" after the 1.2.2
  fix bumped the actual grant to 6. The description now matches what the perk ships.

## 1.2.2 (2026-08-14)
- Path Kit perk now grants 6 Path Kits instead of 3, because completing a road direction actually
  consumes 2 kits per direction in practice, so 3 kits only covered 1.5 directions.

## 1.2.1 (2026-08-14)
- Path Kit now offers only the 4 cardinal directions (North/South/East/West) instead of all 8
  compass directions. The 4 diagonal "Build Path" actions cluttered the action list and are
  removed, along with their orphaned localization rows.

## 1.2.0 (2026-08-14)
- Added three new structure-kit perks, same one-item "Place" pattern as the original nine:
  Well Kit (35 Suns, transforms into vanilla `Well`), Cellar Kit (40 Suns, transforms into vanilla
  `MudHutConstructionDoorEntranceMainCellar`, the Cellar's own self-contained entrance card, same
  pattern already proven by Cabin Kit), and Pit Trap Kit (20 Suns, transforms into vanilla
  `PitTrap`, still needs bait after placing). All three skip vanilla's normal 5-to-11-stage
  construction blueprint entirely, same as every other kit in this mod.

## 1.1.0 (2026-08-14)
- Added Path Kit perk (20 Suns): grants 3 Path Kit items. Each has 8 directional "Build Path"
  actions (N/S/E/W + diagonals) that instantly complete the matching vanilla `Imp_Path*` road
  improvement at the player's current location, via `ProducedCards` + `CompletedImprovements`,
  no C# required. New content, not part of the original CMC extraction.

## 1.0.0 (2026-08-14)
- Initial release. Nine placeable-structure-kit perks extracted from Community Mod Chest:
  Homestead, Cabin Kit, Mud Hut Kit, Log Bed Kit, Furnace Kit, Forge Kit, Oven Kit, Rain Cistern
  Kit, Tanning Pit Kit.
- Homestead Kit's building-material bundle swaps CMC's Advanced Copper Tools-dependent Copper
  Nails for vanilla Metal Nuggets (x20), removing the only cross-mod dependency so this mod runs
  standalone.
