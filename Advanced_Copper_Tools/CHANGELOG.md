# Advanced Copper Tools: Changelog

All notable changes to this mod are documented here.

## [1.18.10] - 2026-10-17

### Added

- **Iron Lantern and Iron Lantern Oilwell.** The Metal Lantern and Lantern Oilwell can now be made from an Iron Sheet as well as the other metals, and the result is named and drawn as iron in all its forms (carried, lit, placed, placed and lit), using Chiwei's iron art. It keeps its metal when you light it, put it out, place it, pick it up or let it burn out.
- **Iron gear rusts when you are wet.** The iron armor, Iron Shield and Iron Large Saw lose durability slowly while you wear them, or carry them in a bag you are wearing, with Wetness above 50 (placeholder rate: about 1% of their durability per fully wet day).
- **Oil Coat.** Drag Fat or Oil onto any of that iron gear (**Grease with Fat** or **Oil**, 15 minutes; it uses up the Fat, or the Oil and gives back its Clay Bowl) to coat it. A coated item does not rust; the coat wears off instead, only while you are wet. The coat never restores durability.
- **Tinned Storage Pot** (Construction → Furniture): a vanilla clay storage pot lined with one tin nugget. Food inside spoils at 50% of the normal rate (a plain clay pot is 70%, a sealed one 40%); it holds 2000 carried and 3000 placed. Its tin lining wears away over about a season, then it turns back into a plain clay pot and keeps everything inside. Numbers are placeholders for tuning.

### Changed

- **Salt-Cured Meat and Salt-Cured Fish now give Proteins 96** when eaten, half of what vanilla dried meat and fish give (192). They gave none.
- **A Metal Lantern or Lantern Oilwell melts back into nuggets of its own metal** (it always gave copper). An alloy lantern made before this version melts to its own metal too.

### Fixed

- **Animated copper items no longer swell to fill the card.** The moving pictures for the lit lanterns and lantern oilwells, the lit stove, brazier and tea station, the cooking cauldron, the warm bathtub and the hot tea kettle were still on the old cropped canvas, so the card stretched them over its whole face and under the name banner whenever they played (the same problem the still pictures had, reported by Chiwei). They now sit exactly where the still picture does.
- **No more false "[ACT-Fix] GetCardUid ... reflection miss" error when a hare or other wandering animal is on your board.** The game builds those animals' board cards without an ID, and the armor check read that as a broken game update and logged an error on every load. Nothing was actually wrong and your armor was never affected; the error is now only raised when the game really changes. Found while checking Banana Monk's save.

## [1.18.9] - 2026-10-15

### Removed

- **The cave system is gone.** The Metal Mines, the Copper Vein Cave, the Iron Vein Cave, the Salt Mine and the Rock Quarry are no longer on the world map, along with their ore veins, the Collapsed Rock Faces you dug through, the Copper and Iron mother lodes, and the dark mines with their Daylight Shaft. Places made by mods now live in the separate Far Territory mod, and the base game's own lands already give each of these resources a home.
- **A save made inside one of the caves loads at the Waterfall Caves**, and anything you left on a cave's board moves there too. This needs CSFF Mod Framework 2.28.5 or later.

### Changed

- **Tin Ore now comes only from panning.** Wherever a river is on the board, the Wearable Metal Pan's **Pan for Stream Tin** takes an hour and now has a 35% chance of one Tin Ore (up from 15%, so the river is a workable supply on its own). Greenstone, bog iron, salt and stone come from their usual places in the base game.
- **The Metal Lantern** is still a light to carry or set down; it just has no mine to light any more.

### Balance pass: closer to the base game

- **Metal armor lasts as long as leather armor.** Every helmet, armor, bracer and greave in all six metals now has 46080 durability, about 185 hits on its body zone, instead of breaking after one. Protection values are unchanged. Pieces in an existing save are raised when the save loads.
- **Metal armor research takes 24 hours** for every piece (it took 4 to 10).
- **The Copper Watering Can no longer has its own Drink button.** It was hidden behind the water's own Drink anyway; drinking from the can works as with any vessel.
- **The Copper-Rim Chamberpot works like going outside.** Urinate and Defecate appear when you need to go, ease the same need, and give a small mood lift (+1 Morale). A pot you defecated in gives a pile of manure when you empty it. Research takes 12 hours.
- **The Metal Wheelbarrow is built on a vanilla wooden Wheelbarrow.** It holds 6000 weight, 4500 of it weightless (was 13500, all weightless), and slows you down while you hold it, like the wooden one. It still does not wear out.
- **Copper Chest:** spoilage inside is 55% of normal (was 20%); it holds 2500 carried and 5000 placed.
- **Copper Bathtub:** a warm or hot bath gives a small morale boost (8 to 12, was 60 to 80), a cold bath 3 to 5, Wash with Soap 4 to 6 and takes 15 minutes.
- **Small Copper Stove:** holds 144 fuel (was 300); an oak log adds 42 and twigs 7; it warms a little less. A full lit stove burns about 36 hours in copper. It still keeps the night safe and carries its fuel when picked up.
- **Large saws blunt with use.** Every bonus pull adds 1 Dullness; at 25 the saw fells at the axe rate until you Sharpen it on a heavy stone, a stone or a whetstone. The bonus is smaller: copper, Ghost Bronze and Tin Bronze fell a large pine in 2 pulls and an oak in 3, White Bronze and the Iron saw a pine in 1 and an oak in 2. Saws in an existing save start sharp.
- **Tea Blending Station:** herbs dry at the vanilla Drying Rack pace unlit and half again as fast lit; Grind All takes 15 minutes for each item it grinds. Research takes 24 hours.
- **Shields:** Copper 80 armor and 5000 durability, Iron 95 and 6500, Ghost Bronze 85 and 5500.
- **Copper Tea Kettle holds 1200 ml**, like the vanilla Cooking Pot (was 2400), so a full kettle still fits on any fire.
- **Copper Cauldron:** no longer slows spoilage of what it holds, and Cook and Boil progress a little slower.
- **Ore Bin** holds 10000 placed (was 15000).
- **Metal Lantern, Lantern Oilwell, Copper Brazier and Rendered Oil** research takes 12 hours.
- **Melt Fat on the Wearable Metal Pan needs an empty clay bowl on the board**, and uses it up: the Oil gives the bowl back when you use it.
- **Perks:** Copper Bathtub and Bathtub Kit cost 2 Moons, Tea Blending Station 3 Moons; Large Saw costs 30 Suns and the saw comes half worn; Building Materials no longer includes a spoon auger.

### Fixed

- The Sap and Resin Tap Spigots' **Nothing** outcome now happens: a spigot's yield can now come up empty (250 of 850), as the base game's taps do; before, it always gave leaves, sticks or wood.

## [1.18.8] - 2026-10-14

### Fixed

- **Card pictures sit where they belong again.** In the previous update every item picture was stretched to fill its whole card, sitting high with the name banner over it. The pictures are back in the game's own framing: centred, low on the card, with space above, like the vanilla cards beside them. Thanks to CHIWEI for the report.

## [1.18.7] - 2026-10-14

### Fixed

- **Salt-Cured Fish and Salt-Cured Meat show the bacteria spoilage icon** instead of the hourglass, as vanilla food does, and their Eat buttons now count their flavours.

## [1.18.6] - 2026-10-14

### Changed

- **The mine locations stay still again.** The slow drift on the Collapsed Wall, the ore lodes and veins and the Mine Daylight Shaft (1.18.5) is off for now. The animated lit lights are unchanged.

## [1.18.5] - 2026-10-14

### Added

- **The mine locations move a little:** the Collapsed Wall, the ore lodes and veins and the Mine Daylight Shaft drift slowly, like the other location paintings. Needs CSFF Mod Framework 2.28.3.
- **Animated card art for lit lights.** The lit Copper Brazier's fire flickers and its smoke drifts, the lit Metal Lantern's glass glows warmer and cooler, and the lit Lantern Oilwell's flame sways. Unlit cards stay still.
- **Animated card art for warm and working copper.** The warm Copper Bathtub (carried and placed) steams and its fire flickers; the lit Small Copper Stove and lit Tea Blending Station glow in the firebox, and the station's kettle steams; the Copper Cauldron's lid rattles while it cooks; the Copper Tea Kettle steams from its spout while it is at least half hot.

## [1.18.4] - 2026-10-13

### Fixed

- **Taking apart copper gear gives back usable Metal Sheets.** The Copper Helmet, Breastplate, Gauntlets, Greaves and Shield, the Copper Cauldron, the Copper Pantry and the Ore Chest returned their sheets at quality 0, unlike every bronze, tin and iron piece. The sheets now come back at quality 50 as copper (same family as Chiwei's report on starting items).
- **The Wheelbarrow and Tea Blending Station perks now list their item as a starting item.** Both listed it as starting equipment, but neither can be worn, so the game dropped it on the ground anyway. Nothing changes in play. The Large Saw perk is untouched.

### Changed

- **Item weights match the base game.** The Shaped Metal Pan Head weighs 100, like every base-game shaped metal head (was 40). Calming Tea, Focus Tea, Warming Tea and Oil weigh 400, a full clay bowl (300) plus what is in it (was 300).
- **Cleaner punctuation in the English text.** Card descriptions, help text, action text, perk descriptions and the player guide no longer use long dashes. The sentences say the same thing.
- Internal: the save-load armor repair now logs a one-time warning for every way a game update could quietly switch it off.

## [1.18.3] - 2026-10-12

### Changed

- **Recipe text matches what the recipes take, in English and Chinese.** A full pass over every blueprint and finished item now lists each recipe's Tin Solder, nails, leather, sinew, mould and hammer. The Copper Gauntlets, Copper Greaves and Wheel Rim say "Copper Bar Needed" to unlock instead of naming a heated lump, and the Wheel Hub no longer asks for "heated" nuggets.
- **Smaller text fixes.** Rendered Oil, Rendered Fish Oil and Render Hemp Seed Oil no longer say "over a fire", since none needs one. The Copper Brazier says it holds two bowls of oil, not "4 charges". The Iron Large Saw and Iron Shield say metal nails. The Wheelbarrow's Chinese text no longer asks for a hammer it does not need, and the Lantern Oilwell's Chinese text says it takes any metal sheet.
- **The Tool Belt's help text lists everything it holds.** It now includes sickles, spoon augers, the whetstone, fire strikers and needles.
- Internal: Grind All logs a one-time warning if a game update breaks the fields it resets, so the old bug where station-ground powder vanished when poured cannot come back quietly.
- Internal: the Mine Darkness and Daylight Shaft helper cards now set `AffectStatsOnlyWhenNotInBackground`, so their In Dark Place effect applies only on the board you stand on. A leak was suspected but not reproduced, so this is a precaution.

## [1.18.2] - 2026-10-12

### Added

- **Copper Sap Bucket.** A copper bucket (Construction → Metal Tools: 2 metal sheets, 1 tin solder and a hammer; researchable once you hold a Metal Sheet) that hangs in a tapped tree. In a Sap Tap it holds 900, the whole three-day flow of a tap with a Copper Spigot. In a Resin Tap (Spigot) it gathers a lump of resin every 18 hours while the resin flows, up to four, and Scrape Out Resin takes one out. Taps you already have keep working with whatever they hold, and the spigot taps' pictures now show the bucket. Not yet tried in a running game.

### Changed

- **The mines produce copper at about the game's own pace (Chiwei's 1004 thread).** The Copper, Tin and Iron veins and the Copper and Iron lodes now strike like the vanilla copper vein. **Mine** takes 2 hours on a vein and 2 hours 30 minutes on a lode (was 45 minutes and 1 hour), needs Stamina 24 or more and costs 12. The second ore now grows from 0 with Smithing 0 to 150, up to 50% for copper, 40% for tin and 30% for iron, instead of a flat 50%, 40% and 30%. **Chip Away** takes 6 hours (was about 2). Lodes still never run out. Salt and Rock veins are unchanged.
- **A worn Wearable Metal Pan weighs nothing itself (Chiwei's 1004 thread).** Carried in a bag it still weighs 300, and up to 1600 of what it holds still counts for nothing.
- **The Metal Lantern turns oil into fuel 15% better than a Ceramic Lamp.** A bowl of oil, or a drag from the Copper Oil Flask, adds 55.2 fuel (the Ceramic Lamp gets 48). Both lamps hold 96 and burn 0.5 per 15 minutes, so a full Metal Lantern still lasts 48 hours and a bowl gives about 27.6 hours of light, up from 24.
- **The Copper-Buckled Tool Belt has six tool slots.** Besides hammers, knives and chisels it now takes sickles, spoon augers, fire strikers, the whetstone and metal or bone needles (threaded or not).
- **Water-Driven Infrastructure's retired rivets, solder and cast sheets are no longer accepted in ACT's slots.** WDI 1.12.3 replaced them with the shared Metal Nail, Tin Solder and Metal Sheet (Chiwei's report, 2026-10-05), and its old cards turn into those within a quarter of an hour. ACT's own retired Iron Nail and Iron Sheet stay accepted. Nothing changes in play.
- **Old Iron Sheet cleanup.** The old `act_iron_sheet` row is gone from `SmeltingRecipes.json`, since the card already becomes a Metal Sheet on its own and the recipe could never fire. The card's Reforging bar and Becomes a Metal Sheet action now have English and Chinese text.
- **Card art in the vanilla form.** 132 card pictures are cropped to the subject and saved as small palette images, so each item fills its card like a vanilla item and the pictures take about a sixth of the space.
- **New and cleaner art from the art review (pictures only).** Salt-Cured Fish is drawn as dried salted fish. The Tool Belt is a closed ring with its buckle and tools, using the vanilla hammer, the vanilla leather sheath and the copper chisel. The Ore Chest shows its ore. The four iron lantern pictures match the other metals' size. The Copper Bathtub kit box shows a towel rack instead of a herb rack, and the bathtub, Tea Station, Copper Spigot and Daylight Shaft are redrawn. Calming, Focus and Warming Tea and Oil are the game's clay bowl filled with each drink. Chiwei's metal art now covers the Small Copper Stove, Large Saw, Iron Large Saw, Forged Pan Blank, Wearable Metal Pan, Copper Cauldron, Metal Lantern and Lantern Oilwell blueprints and the Large Saw perk. White left around the Focus and Calming Tea pictures is gone.

## [1.18.1] - 2026-10-09

### Changed

- **The Ore Bin holds only ore.** Carried and placed, it accepts Greenstone, bog iron (fresh, dried, roasted or crushed), Tin Ore and Flint, and refuses everything else, including food, Salt, Stone and Metal Nuggets. Before, it took anything that was not an animal. Its help text no longer warns about Harsh Wilds' black bear, since the bin holds nothing the bear takes. A bin that already holds other things keeps them until you take them out. Not yet tried in a running game.

### Fixed

- **Allied Partners eat Salt-Cured Meat and Salt-Cured Fish.** Both now count as human food, which is what a Partner's Eat Food duty looks for. Before, a Partner given either never ate it.
- **The empty Copper Bathtub fills from a bowl or bucket of water.** Dropping a container of water on the carried or placed empty tub did nothing, and only Fill from River worked.
- **A Shaped Metal Pan Head cools down once it leaves the forge.** It stayed hot for good, kept its glowing picture, and could never be made into a Wearable Metal Pan (which needs it at 150 or below). It now cools at the same rates as vanilla's metal tool blanks, so a furnace's heat still warms it.

## [1.18.0] - 2026-09-29

Thanks to Chiwei, whose balance proposal is behind the Wearable Metal Pan and Small Copper Stove changes and whose art for every metal gives each metal product its own picture.

### Added

- **Tin, Ghost Bronze and White Bronze armor.** Each suit has a helmet, bracers, greaves and torso armor made only from sheets of its own metal. Hammer a heated bar of the metal on the Bronze Sheet blueprint, and a suit's recipes appear once you hold one of its sheets.
  - **Tin:** head and torso 32.5, arms and legs 16 each, durability 90. The lightest metal suit. No Tin Solder, 2 copper nails a piece.
  - **Ghost Bronze:** head and torso 35, arms and legs 16.5 each, plus +12.5 (head, torso) and +7.5 (each limb) against spiritual attacks, which is how wolves, bears, foxes, badgers and beavers strike. Against those it blocks more than iron. Each piece makes Fear fall 0.25 faster every 15 minutes.
  - **White Bronze:** head and torso 42.5, arms and legs 19 each, +3 Courage per piece, durability 100.
- **Iron Shield and Ghost Bronze Shield, and every shield dismantles.** The Iron Shield (3 iron sheets, plank, 4 nails, medium leather, no Tin Solder) blocks 110 on the shield arm and 160 against spiritual attacks. The Ghost Bronze Shield (3 Ghost Bronze sheets, copper nails, Tin Solder) blocks 95, and 175 against spiritual attacks. All three now have a Dismantle button returning sheets of their own metal, the plank, nails and a usable medium leather, and melt into nuggets of that metal (iron 30, Ghost Bronze 25).
- **Large Saw, nails and Small Copper Stove in every metal.** Each takes its metal from what it is made of (saw and stove from their sheets, which must all match; nail from its nugget) and shows that metal's name and Chiwei's picture. The stove keeps its metal through Place, Pick Up, lighting and burning out.
  - **Large Saw** on the four large trees: copper fells a pine or willow in one pull and a birch or oak in two, and lasts about 100 pulls. Ghost Bronze is the same and lasts about 166, Tin Bronze about 200. Tin is soft: two pulls for a pine, willow or birch, three for an oak, about 66 pulls. White Bronze fells like iron but lasts about 80.
  - **Small Stove:** Sanctuary +75 (copper, Tin Bronze), +80 (Ghost Bronze), +65 (Tin), +90 (White Bronze). A full lit stove burns about 75 hours (copper, White Bronze), 83 (Ghost Bronze), 60 (Tin) or 94 (Tin Bronze).
  - **Nails** differ only in looks.
  - **Existing saves:** saws, nails and stoves you already have, those handed out by other mods, and a stove taken back out of a bathtub or Tea Station all load as copper, with fuel, wear, ash and charcoal kept.
- **Per-metal pictures by Chiwei.** The Metal Sheet, Shaped Metal Pan Head, Wearable Metal Pan, Stove Top and Copper Cauldron show their own metal's picture. A Shaped Metal Pan Head above 150 Temperature shows one glowing picture whatever its metal.
- **Metal Lantern in every metal, from one blueprint.** The Metal Lantern (all four forms) and the Lantern Oilwell take the metal of their sheet and show its picture. Lighting, putting out, placing and burning out keep the metal. Lanterns from before this version are copper.
- **Lit Lantern Oilwell.** The Lantern Oilwell is now a lamp of its own: pour oil in, light it from any fire, and it burns 1 per 15 minutes for 24 hours, giving +10 Light against the lantern's +20.
- **Cast Copper Nails (Chiwei's 1004 thread).** One casting blueprint (Metal & Clay → Metal Crafts: 1 heated copper, tin or bronze bar, 1 mud brick and 1 clay as the mould) makes 6 nails of the bar's metal. A cast nail costs the same metal as a hammered one.
- **Copper Shears (with Sirus23 Mod Collection).** Shear Sirus23's tame sheep before you have iron (Construction → Metal Tools: 1 copper sheet and 1 copper nail, plus a hammer; the recipe appears once you hold a Metal Sheet). They use Sirus23's Shear action and snap after 30 shearings. Without Sirus23 the recipe is hidden at run start (a save that already learned it keeps it). Not yet tried in a running game.
- **Grappling Hook.** Three forged copper prongs on a coil of rope (Construction → Metal Tools: 3 copper nuggets and 1 rope, plus a hammer; the recipe appears once you hold a metal nugget). Carried in your item row it raises Climbing Aid by 72, so every climb is more likely to succeed. It never wears out.
- **Copper Spigot.** A forged copper spout (Construction → Metal Tools: 1 copper sheet and 1 tin solder; the recipe appears once you hold a Metal Sheet). Drag it onto a placed Sap Tap or Resin Tap and choose Fit Spigot: the tap drains a third as fast, so it runs about three days between refreshes instead of one. The bowl, the sap and the flow bar carry over, and Remove Spigot gives the plain tap and the spigot back. Twigs a fitted tap drops now carry its tree's wood.
- **Cautery Iron.** A copper rod with a flat head (Construction → Metal Tools: 1 heated copper ingot and 1 long stick; the recipe appears once you hold a heated metal ingot). Heat it in a fire, drag it onto an open laceration and choose Cauterize: the cut becomes its stitched form at once, and you take one Fire Burn, extra Pain from a new Cautery Burn effect and 30 Stress. It closes a cut at a burn's cost; stitching stays painless. Not yet tried in a running game.
- **Copper-Buckled Tool Belt.** A leather belt (Construction → Metal Tools: 1 copper sheet, 1 medium leather, 4 copper nails and a hammer; the recipe appears once you hold a Metal Sheet) worn in a Tool Belt slot of its own, so it never replaces a knife sheath or spear sheath. Tools in it weigh 200 less (the worn belt 250 less) and still count as on you. A blueprint's ingredient slot still does not take tools from the belt.
- **Bedwarming Pan.** A lidded copper pan (Construction → Metal Tools: 1 copper sheet, 1 long stick, 2 copper nails; the recipe appears once you hold a Metal Sheet). Fill it with embers at a burning fire, and tucked into a log bed or improvised bed it adds +3 Perceived Temperature while you sleep (a large cloth gives +2). The embers last about ten hours, then it turns back into a cold pan.
- **Dark mines with a daylight shaft.** All five caves now get no daylight, in runs already under way too: Light sits at 0 and the screen darkens. Every Mine, Chip Away, Clear the Seam and Dig Through action refuses until you bring light, such as a vanilla torch, a lit Metal Lantern, or a lit Copper Brazier or Small Copper Stove in the cave. Every exit works in the dark. The Metal Mines has a **Daylight Shaft** giving Light 15 from 8:00 to 16:00, so a player caught there without a light can wait for morning and dig out. A save in progress turns each cave dark the first time you enter it.
- **Mother lodes in the Copper and Iron caves.** Each holds one collapsed seam. Clear the Seam with a pickaxe, shovel, axe, antler or knife (2 hours a strike, 18 strikes, 2 tool uses each) and it becomes a lode you can Mine or Chip Away forever, while ordinary veins still run out. **New caves only:** a save that already visited the Copper or Iron cave gets no lode.
- **Salt-Cured Fish.** The fish twin of Salt-Cured Meat (Survival → Support: 1 raw lean fish and 2 Salt; the recipe appears once you hold Salt). It spoils far slower than dried lean fish (1800 against 840), gives Satiation 19, Fullness 7, Calories 18 and Hydration -5, adds Savoury and Salty in a dish, and the salt costs a little thirst.
- **Metal Sheets and Metal Nails trade by their metal (Chiwei's 1004 thread).** A copper Metal Sheet trades at 2250 and a copper Metal Nail at 375, and each rises with the metal as vanilla's bars do: an iron sheet is worth four times a copper one. The Cast Stove Top is 2250. Before, every sheet sat at about 100.
- **Allied Partners eat Salt-Cured Meat, Salt-Cured Fish and rendered Oil.** Teas and the Watering Can carry none of this, so a Partner never drinks them.
- **Copper Bathtub works with Herbs and Fungi's Bath Soak.** With that mod, a soak bundle dragged onto a filled or warm tub runs Soak in the Tub.

### Changed

- **Metal Sheet, Metal Nail and Tin Solder moved to CSFFModFramework 2.28.0.** The three cards, their seven blueprints, pictures, text and furnace rows now ship in the framework under the same UniqueIDs, so other mods can use them without ACT and a save keeps its parts and researched blueprints. ACT's recipes use them as before. **Needs CSFFModFramework 2.28.0 or later.**
- **One Metal Sheet and one Metal Nail in every metal, iron included (Chiwei's 1004 thread).** An Iron Sheet is now the Metal Sheet at Metal Type 200 and an Iron Nail the Metal Nail at Metal Type 200, drawn with the old iron picture. UniqueIDs are unchanged, so saves and research keep them. Every iron recipe takes Metal Sheets of iron only, and a nail of any metal fits every nail slot. Eight recipes whose product has no iron form (Small Copper Stove, Large Saw, Copper Cauldron, Copper Oil Flask, Forged Pan Blank, Lantern Oilwell, Metal Lantern, Wheelbarrow Bucket) now refuse an Iron Sheet. Smelting an iron sheet returns 6 iron nuggets (was 5) and an iron nail 1. A Metal Nail weighs 30 whatever its metal (iron nails were 15). **Old cards:** retired Iron Sheets and Iron Nails still load as "Iron Sheet (old)" and "Iron Nail (old)", turn into the new cards within a quarter of an hour, and have a Reforge button that does it at once.
- **Iron is never cheaper than copper (Chiwei's 1004 thread).** The Iron Sheet takes 6 iron nuggets instead of 3, and Forge Iron Nails takes 4 nuggets for 4 nails instead of 2. Metal Sheet and Iron Sheet weigh 100 (were 150 and 170).
- **Small Copper Stove is built in two stages, and the Cast Stove Top Mold step is gone.** Stage 1 casts its own top (a small molten crucible, kept and emptied, 2 mud bricks and 2 clay) and stage 2 assembles it (4 metal sheets, 4 copper nails and a tin solder, plus a hammer). The Stove Top Mold and Stove Top still load so old saves keep them, and the Stove Top gains **Cut Up** into 2 sheets of its metal. Dismantling the stove returns its 4 sheets instead of a copper top.
- **Small Copper Stove weighs 600 and places and picks up in about 3 minutes** (was 300 and 30 minutes). It already kept its fuel and metal.
- **Copper Cauldron is 25% larger than the clay cauldron and weighs 600** (was 1200). It takes an 8000-weight basket (was 3000) and holds 6000 ml (was 6240). It also counts as a medicine-brew station for Herbs and Fungi, like the clay cauldron.
- **Large Copper Saw costs 3 metal sheets, 2 wood and 2 copper nails** (was 2 sheets and 4 nails) and weighs 375 (was 200).
- **Copper Tea Kettle costs 2 metal sheets (was 3), and its water weighs nothing.** A full kettle weighs 180, so it fits a Campfire or the stove.
- **Wearable Metal Pan and Small Copper Stove rebalanced, from Chiwei's proposal.**
  - The Forged Pan Blank is hammered from 1 Metal Sheet of any metal instead of 4 copper nuggets, and takes that sheet's metal.
  - The Wearable Metal Pan takes 3 twine and 1 wood in place of rope, a plank, small leather and copper nails (the shaped pan head and Tin Solder stay). Dismantling returns the pan head, 3 twine and 1 wood.
  - The pan weighs 300 (was 30), holds 1600 (was 1500) and 1200 of liquid, and up to 1600 weight of what it holds no longer counts toward your load, as with the vanilla Cooking Pot.
  - The hammer slot on the pan blank, wearable pan and stove takes only a forge hammer, mallet, small stone, antler or shovel. It used to take knives and axes too.
  - Holding a Metal Sheet unlocks all four together: the pan and stove for research, and the Forged Pan Blank and Cast Stove Top Mold with no research.
  - The stove holds 600 weight, like a Campfire (one cooking pot or two clay bowls), instead of 2 slots, and keeps its fuel when picked up.
  - A placed stove, lit or not, gives +75 Sanctuary.
- **Lamp oil is worth 48 fuel, and the lantern burns twice as long (Chiwei's 1004 thread).** Every oil consumer (Metal Lantern, Copper Oil Flask, Copper Brazier) takes 48 fuel per bowl, up from 24. The Metal Lantern holds 96 and burns 0.5 per 15 minutes, 48 hours a fill (was 72 at 1 per tick). It is now built from 1 metal sheet, 1 Lantern Oilwell, 1 copper wire, 1 parchment, 1 fat and a hammer (was 2 sheets, an oilwell, a stone and Tin Solder), and no longer mentions glass. The Oil Flask holds 4 bowls (192, still two lantern fills), and the Brazier takes 2 bowls to fill, the same 16 hours a fill for half the oil. Fuel in lanterns and oilwells already in a save is not rescaled, so a half-full lantern of 36 is now 37% full.
- **Panning for Stream Tin moved to the Wearable Metal Pan.** The Shaped Metal Pan Head never wore out, so it panned tin for free without limit. The Wearable Metal Pan loses 1 of its 100 uses per try.
- **"Bronze" armor is now Tin Bronze armor** and takes Tin Bronze sheets only. It used to take any sheet from Ghost Bronze to White Bronze. Pieces you already have keep working.
- **Copper armor and the Copper Shield take copper only.** They used to accept a sheet or heated bar of any metal, iron included.
- **Armor recycles into its own metal.** Smelting a Tin, Ghost Bronze, Tin Bronze, White Bronze or iron piece, or the Iron Large Saw, returns nuggets of that metal, and dismantling an alloy piece returns sheets of its own metal (quality 50).
- **The Copper Bathtub washes like the river.** Warm and Hot Baths now take Filth down by 500 (was 200) and a Cold Bath and Wash with Soap by 400 (was 200 and 100). All four also take Rash and Wolfsbane Exposure down by 100 and Mud Applied and Soothing Cream Applied by 50, and leave you fully wet. Morale, Stress and warmth effects are unchanged. A stronger bath also cures more of anything that reads Filth, such as the Harsh Wilds fleas.
- **The Copper Chest and Ore Bin no longer claim to be animal-proof.** Vanilla wildlife cannot raid them, but the Harsh Wilds autumn black bear destroyed Dried Meat in a placed Copper Chest. The text now says so.
- **Render Hemp Seed Oil is hidden without Herbs and Fungi,** like Copper Shears without Sirus23. It could never unlock there. Rendered Oil and Rendered Fish Oil still work, and a save that already learned the recipe keeps it. With Herbs and Fungi nothing changes.
- **No plugin dependency on Herbs and Fungi.** Load order never mattered, so the declaration is gone. Nothing changes in play.
- **The Copper Watering Can's text says it waters garden plots.** It always could: each watering takes 300 ml, so a full 1500 ml can waters five times.
- The Bronze Sheet blueprint's text names Tin, which it always accepted.
- With Community Mod Chest 1.69.3 or later, the Academy's Armorer course teaches every armor recipe, and passing Metallurgy also hands over the Bronze Sheet recipe.

### Fixed

- **Piles of Large Saws and Lantern Oilwells can be spread out.** Both come in one card per metal and piled with no expand toggle, so you could not pick the bronze saw out of a pile holding a copper one.
- **A Partner on Fire keeping can pick the Copper Stove, Copper Brazier and Metal Lantern refuel actions.** The duty only performs a feed action that carries certain action tags, and these cards had none, so it could be assigned but never refuelled anything. They now carry the same tags as the Campfire feeds. The brazier and lantern may still not be refuelled, because the duty picks the fire to tend by a tag only their lit cards carry, and only their unlit cards have refuel actions. The mod description no longer says a Partner tends their fuel.
- **Salt-Cured Meat tastes of something.** It carried no flavour. It now adds Savoury and Earthy at vanilla's strengths, plus Salty at medium strength.
- **Tea Station's Grind All keeps freshness and quality, and takes its time.** Every herb used to come out as brand-new powder at quality 0, and the button cost no time. Each herb now grinds as its own hand grind does (Dry Frostleaf at quality 100 comes out 150, as with a stone mortar), and the action takes 30 minutes.
- Text fixes from the release audit: the Chinese Shaped Metal Pan Head and Wearable Metal Pan descriptions were already right but their record of the English was stale, and the README now names every load-time pass and the one daylight entry ACT adds to the vanilla Light stat.

---

## [1.17.3] - 2026-09-28

Thanks to Chiwei, whose report is behind this release.

### Fixed

- **Trait starting items are usable.** Bathtub Kit, Building Materials and Wheelbarrow Kit gave Planks of no wood, Small Leather no recipe accepts, Large Cloth at quality 0 and a Spoon Auger at quality 0. They now start as oak Planks, Small Leather (quality 50), Large Cloth at quality 50 and a copper Spoon Auger at quality 50, and the Wheelbarrow Bucket is copper at quality 50. Needs CSFFModFramework 2.28.0.
- **Dismantling returns usable materials.** Planks, leather, Large Cloth and the Wheelbarrow Bucket that come back from dismantling armor, the Wearable Metal Pan, the Wheelbarrow and its Handles, the Copper Bathtub, the pantries, the Ore Bin and the Tea Blending Station are oak Planks, quality-50 leather and cloth, and a copper bucket. A dismantled Copper Stove returns a copper Cast Stove Top.
- **The Wheelbarrow Bucket and the Metal Sheet carry their metal.** Both show a Metal Quality, and the bucket a Metal Type, taken from what they were made from. Smelting the bucket, a Metal Sheet, Iron Sheet, Copper Cauldron, Stove Top Mold or Cast Stove Top returns nuggets of its own metal instead of quality-0 copper (an Iron Sheet used to melt back into copper). Sheets and buckets made before this update keep quality 0. The smelting half needs framework 2.28.0.
- **The Copper Pan Blank takes the metal of its nuggets.** It used to come out as Metal Type 0 at quality 0.

---

## [1.17.2] - 2026-09-27

### Changed

- **The Copper Shield out-blocks the wooden Shield against every kind of blow.** It lacked the wooden Shield's +50 against spiritual attacks, so it blocked less than the wooden one there. It now has the same +50: 90 armor on the shield arm, 140 against spiritual attacks.

## [1.17.1] - 2026-09-27

### Changed

- **Ore Chest renamed Ore Bin, with its own art.** It is drawn as an open-topped copper bin heaped with ore, where it had borrowed the Copper Chest picture. Bins already built or carried keep working.

### Fixed

- The Ore Bin blueprint description now lists the 1 Tin Solder its recipe has always needed.

## [1.17.0] - 2026-09-26

### Added

- **Iron Large Saw.** Forged from 2 Iron Sheets, 2 wood and 4 iron nails with a hammer (Construction → Metal Tools, researchable once you hold an Iron Sheet). It adds -50 Progress on the four large trees where the copper saw adds -25, so a large pine, willow or birch falls in one pull and an oak in two. 150 uses; melts back down for 20 nuggets.
- **Copper Shield.** A copper-faced shield held in hand like the wooden Shield: 90 armor on the shield arm against any blow, 8000 durability. Made from 3 copper sheets, 1 plank, 4 copper nails, 1 medium leather and 1 Tin Solder; melts back down for 22 nuggets. It keeps protecting after a reload.
- **Pan for Stream Tin.** At any river, the Wearable Metal Pan can pan gravel for an hour with a 15% chance of one Tin Ore, losing 1 use per try. The Tin Vein Cave stays the fast source.
- **Warm Up and Wait by Fire on the lit Copper Brazier**, the same two actions the Small Copper Stove already had.
- Own art for the four Bronze armor pieces and their blueprints, the Salt Vein and the Rock Vein, which borrowed other pictures.

### Changed

- **Copper fires behave like fires.** While lit, the Small Copper Stove and the Copper Brazier dry you out, keep bugs down (the brazier also mosquitoes) and light the area, with the values of the vanilla fires, and make you easier to spot (Stealth -150). The stove gave none of these before; the brazier gave light only.
- **Any lamp oil fuels the Metal Lantern, the Copper Brazier and the Copper Oil Flask,** not only Rendered Oil. With Herbs and Fungi installed that includes its hemp seed, linseed, peanut, truffle and seven herbal oils. Each clay bowl adds 24 fuel and gives the bowl back.

### Fixed

- Text: the lit Copper Brazier's help text claimed it "boosts morale", which it never did, and the Large Copper Saw's help text left Tin Solder out of its own recipe.
- The README's Crafting Tabs table was missing several blueprints and no longer says reaching the Tin Vein Cave is required before most of the mod can be built.

## [1.16.11] - 2026-09-26

### Fixed

- **Sheets, the Copper Cauldron and the Copper Oil Flask keep the metal they were made from.** The game added a recipe's carried-over metal type to the product's own starting value instead of replacing it, so every Metal Sheet, Bronze Sheet, Copper Cauldron and Copper Oil Flask came out as White Bronze whatever went in, and copper-made sheets could enter the Bronze armor recipes. A copper bar now makes a Copper Sheet and a Tin Bronze bar a Tin Bronze Sheet. Items crafted before this update keep the metal they were given.

## [1.16.10] - 2026-09-26

Thanks to Chiwei, whose report is behind both fixes in this release: ACT overwriting the vanilla
fire capacities, and alloy sheets that could not be picked out of a pile.

### Fixed
- **Vanilla fires keep their own capacities again.** Since 1.7.5 (May 2026), ACT raised every
  vanilla fire it lets the copper kettle and cauldron onto to a capacity of 2580: Campfire (600),
  Fireplace (1200), Fire Pit (2400) and the lit Oven (1200), lit or out. The unlit Oven was not on
  the list, which is why the Oven's capacity jumped when it was lit. The two Hearths, which hold two
  slots in vanilla, were turned into 2580-weight inventories the same way; they have their two slots
  back. The number came from a wrong
  estimate of the kettle's full weight, counting its water at 1 weight per unit where water weighs
  0.33. ACT no longer changes any fire's capacity, so the vanilla cooking ladder is back: a Campfire
  takes two clay bowls or one cooking pot, a Fireplace one clay cauldron, a Fire Pit two.
- **The Sauna Stove no longer holds items.** ACT gave it the same 2580 capacity, and since its
  vanilla filter is empty it then accepted any card, food included, and did nothing with it. ACT no
  longer touches the Sauna Stove at all.
- **ACT no longer widens what vanilla fires accept or heat.** The copper kettle and cauldron are let
  onto fires, and heated there, by name only. Before, each fire was also opened to several vanilla
  container tags and the heating recipe matched a vanilla tag, so vanilla items carrying those tags
  could be let in or heated as well.
- **Metal Sheets of different metals can be picked apart.** Copper, Ghost Bronze, Tin, Tin Bronze and
  White Bronze sheets are one card named after its metal, so they pile together, and the pile had no
  expand toggle: there was no way to drag out a bronze sheet for the Bronze Armor Set. The pile now
  has the toggle, as vanilla metal nuggets and bars do. Cast Stove Tops, Stove Top Molds and shaped
  pan blanks, which also come in several metals, get the same toggle, and so do Copper Nails, to
  match Iron Nails. There are no alloy nails: Copper Nails are hammered from copper-grade nuggets only
  and carry no metal type, so every nail is the same.

### Changed
- **The Copper Cauldron weighs 1200, the same as the vanilla clay cauldron (was 520).** It follows the
  same fire rules as a result: one fits a Fireplace, two fit a Fire Pit, and a Campfire is too small.
  It is also kept out of the Oven, which refuses the clay cauldron by name. It is heavier to carry.
- **A full Copper Tea Kettle needs a Fireplace, Fire Pit or Oven.** The kettle weighs 180 and its
  water counts, so full of water it weighs about 970, more than a Campfire's 600. A Campfire takes it
  about half full, and the Copper Stove holds it at any fill. With the old 2580 capacities it fit
  anywhere.
- In an existing save, a fire already holding more than its restored capacity keeps only what fits when the
  save loads: the game puts a container's cards back one at a time and drops the one that no longer fits onto
  the board, so nothing is lost but the extra cauldron or pot is outside the fire. (Corrected 2026-10-13: this
  entry used to say the fire keeps everything it held. A harness probe put two vanilla Cooking Pots, 600 each,
  into a vanilla Campfire and reloaded: one stayed, one was dropped, so it is vanilla load behaviour and not
  something this mod does; tracker T2.283 and T2.462.)

### Docs
- The README, the Player Guide and the cauldron's in-game text (English and Chinese) now say which
  fires take the kettle and the cauldron. The README's compatibility note, which said ACT changes
  nothing else on vanilla cards, now lists exactly what ACT adds to each fire.
- **The Player Guide's recipes match the blueprints again.** Most had drifted: every recipe that
  takes Tin Solder now says so, and the sheet, stove, pan, saw, armor, wheelbarrow parts, lantern,
  oil, chest and tea station recipes, the armor's crafting tab and unlock, the Tea Station reservoir
  (8 bowls, not 300) and the smelting recovery table (read from the mod's own smelting table; the
  kettle and wheelbarrow bucket it listed are not in it) are corrected.

## [1.16.9] - 2026-09-23

### Docs
- **The README no longer says the Tea Blending Station must be empty before you pick it up.** It
  never had to be: picking it up sets anything still in its six slots down beside you, and the
  README now says so. The troubleshooting line that told you to drain the reservoir first is
  corrected too; the only requirement is that the stove is out. The Player Guide had the same
  mistake and is corrected the same way, and its Copper Bathtub section no longer says the tub
  must be empty before Pick Up: a full or warm tub can be picked up too. No gameplay change.

## [1.16.8] - 2026-09-18

### Changed
- **Salt-Cured Meat has its own picture.** It was borrowing the game's Cooked Meat picture; it is
  now a thick red slab caked in coarse salt, on both the item and its blueprint.

## [1.16.7] - 2026-09-18

### Changed
- **Download is about a third of its previous size: 37.2 MB to 11.8 MB.** Two separate causes, both
  in packaging rather than in the mod. The build script wrote a second, byte-identical copy of every
  card image into `Resource/Texture2D/` alongside `Resource/Picture/`, which nothing ever read, so
  every release archive carried the whole art set twice. Separately, the three Copper Bathtub images
  had shipped at 2400x1792 instead of the 512-wide size every other card in this mod uses. Both are
  fixed. No gameplay, content or balance change, and the bathtub art is unchanged apart from its
  resolution.

## [1.16.6] - 2026-09-08

### Docs
- **README under-claimed a confirmed feature (docs-honesty, under-claiming direction).** The v1.15.9
  entry still read "Not yet verified in-game" for Copper Stove / Copper Brazier / Metal Lantern
  Firekeeping-duty compatibility, but the playthrough tracker confirmed it in-game on 2026-08-15
  (T2.85). Updated the v1.15.9 entry and added a player-facing sentence to the Small Copper Stove,
  Copper Brazier and Metal Lantern sections stating a recruited Partner tends their fuel under the
  vanilla Fire keeping duty. Also noted the Ownership panel on the Brazier and Lantern (who may claim
  and refuel them) is still unconfirmed (T2.212, pending). No code change.

## [1.16.5] - 2026-09-06

### Fixed
- **Bronze/White-Bronze armor is now actually craftable as designed, and the Copper tier is no
  longer strictly obsolete at the same unlock point.** `MetalSheet.json`'s Metal Type stat
  (`SpecialDurability4`, plus the unused `SpecialDurability2/3`) was authored with field names that
  don't exist on the game's `DurabilityStat` type (`DurabilityName`/`MaxDurability`/`StartDurability`/
  `Restricted`/`IsHidden` instead of the real `Active`/`FloatValue`/`MaxValue`/`RestrictToSpecificValues`/
  `ValidValues`/`HidingOptions`), so it silently deserialized inactive: every Bronze armor blueprint's
  bronze-grade metal-type gate was a no-op, and a plain Copper Sheet satisfied it just as well.
  Rewrote the field names so Metal Type is genuinely active (`ValidValues` 100/110/120/130/140), and
  added a new **Bronze Sheet** blueprint (`act_bp_bronze_sheet`, Metal & Clay -> Metal Crafts) that
  hammers a heated Ghost Bronze/Tin Bronze/White Bronze bar into the same sheet card, now correctly
  tagged bronze-grade (110-140) - the only way to produce a sheet that satisfies the Bronze armor
  recipes. A plain Copper Sheet (Metal Type 100, from the base Metal Sheet blueprint) no longer
  qualifies. Not yet verified in-game.
- **Cross-mod safety**: WaterDrivenInfrastructure's Cast Metal Sheet is now tagged Metal Type 100
  (copper-grade), so ACT's sheet-interchangeability patch can no longer hand WDI players a sheet that
  bypasses the Bronze gate above. WDI needs its own version bump to ship this - tracked separately.
- **All four Bronze armor blueprints' research-unlock gate now actually requires a bronze-grade
  sheet.** `Bp_BronzeHelmet.json`, `Bp_BronzeBreastplate.json`, `Bp_BronzeGauntlets.json`, and
  `Bp_BronzeGreaves.json` gated "researchable" on holding ANY Metal Sheet (no metal-type filter),
  while their own `UnlockConditionsDesc` said "Bronze Sheet Needed" and the crafting-time gate
  correctly required Metal Type 110-140. A plain Copper Sheet could prematurely unlock research on
  all four. Added the same `Special4Range: {110, 140}` filter the new Bronze Sheet blueprint already
  uses, so the unlock text now matches what actually gates it.
- **Bronze Armor (torso) now requires medium leather, matching its own recipe everywhere else.**
  `Bp_BronzeBreastplate.json` required `GpTag_LeatherSmall` while README and the item's own dismantle
  recovery (`SkinLeatherMedium`) both said medium - a copy-paste leftover from the Helmet recipe that
  let a player upgrade small leather to medium for a Tin Solder and some build time. Now requires
  `GpTag_LeatherMedium`, matching Copper Armor's equivalent recipe.
- **Removed the non-functional armor quality-scaling scaffolding instead of leaving it disclosed as
  broken.** All 13 armor items' `ArmorValueDurabilitiesMultiplier` block read from `SpecialDurability2`
  (Metal Quality), which every ACT armor item declares inactive - the multiplier could never move off
  its 1.0x floor, and the in-game `EffectName` text ("Armor (+X) interpolated by quality from 1 to
  1.5") kept advertising scaling that never happened. Cleared the multiplier array, disabled the
  matching `EffectScalesWithDurabilities` block, and reworded `EffectName` to the flat value it
  actually applies (e.g. "Armor (+30)"). Also removed `GameLoadPatch.cs`'s
  `RepairArmorMultipliers`/`FixArmorMultiplier` - a 1.16.4 fix that only ever restored this same
  no-op entry, and would otherwise have kept re-adding the multiplier block this pass just removed.
- **Armor-repair reflection misses are no longer silent.** `Patcher/GameLoadPatch.cs`'s
  `FindEquippedArmorCards` returned an empty list on ANY reflection miss with no breadcrumb, so a dead
  `CharacterWindow` / `EquipmentSlotsLine` / `Slots` chain was indistinguishable from a legitimate
  "no armor equipped": `RepairArmorCards` reported both as `changed 0`, and a field rename after a game
  update would have disabled the equipped-armor half of the save/reload repair with zero log output.
  The file already had the right machinery for this (a per-cause `HashSet` plus `WarnArmorRepairOnce`,
  carrying a comment describing this exact hazard) wired into 2 of its 3 reflection exits; the third had
  none. Added `CharacterWindow-null`, `EquipmentSlotsLine-null` and `EquipmentSlots-null` so the
  warn-once covers every early return rather than only the catch. No behaviour change when reflection
  succeeds; a failure is now an error line naming the cause. Checked while in there: ACT is clear of the
  EA 0.67x `GraphicsManager.GetSlotForCard` arity break that is throwing in a sibling mod, because it
  never calls that method and its only reflected method call, `UpdatePassiveEffects`, matches the live
  zero-arg card-level overload.

### Changed - documentation / honesty corrections
- **`ModInfo.json`'s Description no longer claims armor tracks the metal it was built from.** All 13
  ACT armor items (Copper/Bronze/Iron) declare their Metal Type stat inactive and carry no
  metal-variant names - only the Oil Flask and Cauldron actually track metal. Corrected to say so.
- **`ModInfo.json` and README no longer call the bathtub's Wash with Soap "a bigger cleansing and
  mood boost."** It's smaller than the free Warm Bath on the same card (Filth -100 / Morale +30-40,
  and consumes a Soap, versus the free bath's Filth -200 / Morale +60-80 plus three more stats) - its
  real advantage is that it doesn't require the fire to be stoked. Reworded everywhere this claim
  appeared.
- **README's Metal Sheet section (Base Materials) no longer contradicts its Bronze Armor Set
  section.** Both now describe the real, current path: copper-grade sheets from the base Metal Sheet
  blueprint, bronze-grade sheets from the new Bronze Sheet blueprint above. Also corrected a
  README claim that armor value rises with durability/quality - it doesn't; see the Fixed section of
  [1.16.4] above for the corrected record on that.
- **Disclosed undocumented art reuse.** Bronze Helmet/Bracers/Greaves/Armor (and their blueprints)
  reuse the Copper set's sprites, and the Ore Chest reuses the Copper Chest sprite - both now noted
  in the README next to the Iron tier's distinct art, matching how reuse is disclosed elsewhere in
  this doc (e.g. the Cave Prospector perk icon reusing tin vein art). No new art shipped this pass.
- **Removed the "Metal Pan" row from README's Character Perks table.** That perk (backed by
  `Perk_MetalPanTester.json`) was deleted in [1.16.4] as unreachable dev content, but the README row
  describing its starting kit was never removed, so the README kept advertising a 5-Suns perk that
  had not existed for a release already. 7 perks now ship and 7 are documented.
- **The bathtub's Wash with Soap "bigger cleansing and mood boost" claim is now actually reworded
  everywhere it appears, not just in ModInfo.json.** The prior line item above was incomplete: the
  same phrase was still live in README's Copper Bathtub Actions list and in the card's own in-game
  `ActionDescription` (JSON + both localization CSVs). All three now read "a quick clean and mood
  boost that doesn't need the fire lit."
- **README's `GameLoadPatch` description no longer says "copper armor" only.** The armor-repair net
  it describes has covered all 13 Copper/Bronze/Iron items since [1.16.4]; the README wording was
  never updated to match.
- **Smelting Recovery table now lists all smeltable items.** Added the 8 rows that were missing
  despite having real `SmeltingRecipes.json` entries: Iron Sheet, Iron Helmet, Iron Bracers, Iron
  Greaves, Iron Armor (Male/Female), Copper Watering Can, and Copper-Rim Chamberpot.
- **README no longer advertises the armor-multiplier repair that this same release deleted.**
  `README.md`'s Harmony-hook list still described `GameLoadPatch` as a postfix that "repairs a missing
  `ArmorValueDurabilitiesMultiplier` entry on every ACT armor piece" - but the fix above removed
  `RepairArmorMultipliers`/`FixArmorMultiplier` outright, so that clause described code that no longer
  exists. Two same-release fixes left unreconciled: one corrected the tier scope (copper -> all three
  tiers), the other deleted the mechanism being scoped. The bullet now describes what the postfix
  actually does, and additionally documents `PatchSheetInterchangeability` (ACT Copper/Iron Sheet slots
  also accept WDI's matching Cast Sheet, tier-locked, no-op without WDI), which shipped undocumented.
  The player-facing behavior it backs - armor keeps mitigating across a save/reload, via
  `RepairArmorCards` + `RefreshArmorPassiveEffects` - is unchanged and still real.
- **Copper Cauldron no longer claims "six cooking slots".** `CopperCauldron.json` ships an empty
  `InventorySlots`/`InventorySlotsWarpData` and is governed by `MaxWeightCapacity: 3000`, which is the
  standard vanilla weight-limited container shape (vanilla `Basket`, `Chest`, `Shelf` and `CookingPot`
  all ship it). Capacity was never broken, but the item count is not capped at six: it depends on
  ingredient weight. README's Overview bullet and Features list now say so. The Tea Blending Station's
  "six herb slots" is untouched and remains accurate - that one IS a real 6-entry `InventorySlots` array.
- **Tea Station's per-card transform log demoted to Debug.** `Patcher/TeaStationPatch.cs` logged
  `[TeaStation] Transformed -> {uid}` at Info once per converted card, so a single "Grind All" sprayed
  the log, against CLAUDE.md's per-item-loop logging norm. The line's own comment set the release
  condition ("Info until the 1.15.8 pour-quantity fix is play-verified"), and that gate is satisfied:
  the playthrough tracker records T2.83 = pass and no retrospective names the line as a confirmation
  signal. Now `LogDebug`, stale comment removed.
- **`IronNailSmeltPatch` renamed to `TinOreSmeltPatch`.** The class handled only `act_tin_ore`
  smelting and nothing about iron nails, so a contributor searching for iron-nail logic by filename
  landed in the wrong file. Renamed the file and the class (`git mv`, so history follows) and updated
  the `Plugin.cs` registration and the README hook list; the README entry now also mentions the
  Metal Quality (SpecialDurability2) carry-over it was already doing. No behavior change. The dead
  `IronOreUid` constant this class used to carry was already gone.
- **Ore Chest README no longer implies a food filter it does not have.** The description said the
  crate was for bulk raws "rather than food", but its `InventoryFilter` only excludes the four
  animal-size tags, so food fits fine. Reworded to state the real mechanic: food fits but gains
  nothing, because the Ore Chest has no spoilage protection (that is the Copper Chest's job).
- **Armor-repair reflection now leaves a breadcrumb on every early return.**
  `FindEquippedArmorCards` returned an empty list on a `CharacterWindow` / `EquipmentSlotsLine` /
  `Slots` reflection miss with no log, so `RepairArmorCards` reported "changed 0" - indistinguishable
  from a genuine "no armor equipped", meaning a field rename after a game update would silently
  disable the equipped-armor half of the repair. Added three per-cause `WarnArmorRepairOnce`
  breadcrumbs so the warn-once covers every early return, not just the catch. Logging only, no
  behavior change.

### Known state
- `EffectScalesWithDurabilities` blocks remain on all 13 armor items with `Active: false`. This is
  deliberate and matches vanilla: all 2709 vanilla `PassiveEffects` entries sampled carry the key, so
  the disabled-in-place form IS the vanilla shape and deleting it would deviate from it. The defect
  that mattered (an `EffectName` advertising "interpolated by quality from 1 to 1.5" that could never
  happen) was fixed above by rewording to the flat value.

## [1.16.4] - 2026-09-01

### Fixed
- **Bronze and Iron armor now get the same save/reload combat repair as Copper.** The armor repair
  net (re-adds modded armor to `GameManager.ArmorCards` after a save load and at encounter start,
  refreshes passive effects) only listed the 4 Copper UIDs - the Bronze (4 items) and Iron (5 items,
  incl. both breastplate fits) tiers shipped later were never added, so their armor dropped out of
  the game's combat armor list, and stopped mitigating at all, after a save/reload. All 13 armor
  items are covered now. Internal repair methods renamed tier-neutral. Not yet verified in-game.
  **Correction, 2026-09-06 (see [1.16.5]):** this entry originally also credited the same patch with
  restoring an emptied `ArmorValueDurabilitiesMultiplier` as an armor-quality-scaling fix. That part
  was a no-op - the multiplier reads a Metal Quality stat that every ACT armor item declares
  inactive, so it always interpolates to the same 1.0x floor whether the array is present or empty.
  Removed that claim; see [1.16.5] for the corrected record.

### Removed
- **`Perk_MetalPanTester` deleted (dead dev content).** The hidden "Metal Pan" character-creation
  perk had `HiddenUntilUnlocked` with empty unlock conditions - impossible to unlock or ever see
  in-game. Leftover dev-testing perk (same pattern as the previously removed Cave Prospector);
  flagged by the 2026-09-01 fleet feature audit. Its localization rows were removed with it. No
  player-visible change.

## [1.16.3] - 2026-08-25

### Performance
- **Combat-encounter copper-armor repair no longer rescans the whole board on every hit.**
  `EncounterArmorRepair_Prefix` (fires on `EncounterPopup.GenerateAndApplyPlayerWound`, i.e. every
  wound during every fight) walked the full live-card list with reflection on each call. Switched to
  the framework's `CardFinder.AllCards()`, which caches that same scan and only re-walks it when the
  board's card count actually changes: repeat wounds within a fight (and fights on an unchanged
  board) now hit the cache instead of re-scanning. No behavior change, same cards found.
- **Tin-ore smelt metal-type/quality tagging no longer re-walks the class hierarchy by reflection on
  every smelt.** `IronNailSmeltPatch` had its own hand-rolled `AllCards` field lookup (run twice per
  smelt); replaced with the same cached `CardFinder.AllCards()` the sibling `IronVeinQualityPatch`
  already used.

## [1.16.2] - 2026-08-24

### Removed
- **Cave Prospector perk removed.** The Collapsed Rock Face gates it described have applied to every
  character by default since v1.15.6; the perk itself controlled nothing, and unlike Forest Scout
  in HerbsAndFungi it was never given a compensating stat bonus, so its 1-Star cost bought literally
  no mechanical effect. The cave network (portal + dig-through access) is unchanged; only the
  do-nothing perk pick is gone.

## [1.16.1] - 2026-08-16

### Fixed
- **Blank card art on Rendered Fish Oil and Salt-Cured Meat blueprints.** Both referenced sprite
  names that don't exist (`FishMeatFattyRaw`, `ClayBowl`) and rendered as a blank/locked card.
  Rendered Fish Oil now uses the vanilla Clay Bowl art (`Bowl_Clay`) to match its own "render in a
  clay bowl" description; Salt-Cured Meat's blueprint and finished item now use the vanilla Dried
  Meat art (`Meat_Cooked`).

## [1.16.0] - 2026-08-16

### Added
- **Ore Chest**: a high-capacity, animal-safe sealed metal crate for bulk mining raws (Greenstone,
  Bog Iron, Tin Ore, Salt, Stone). No spoilage protection, just a lot of weight-based storage.
  Craftable at Construction → Furniture.
- **Salt-Cured Meat**: raw meat cured in Salt into a slow-spoiling travel ration; gives the Salt
  Mine a self-contained ACT payoff beyond feeding vanilla cooking. Craftable at Survival → Support.
- **Bronze / White-Bronze armor tier**: Bronze Helmet, Bracers, Greaves, and Armor, slotting
  between the Copper and Iron tiers. Gated on a bronze-grade metal sheet (any of Ghost Bronze / Tin
  Bronze / White Bronze) via the existing SD4 metal-type system, not a new sheet item. Furnace
  recyclable like every other ACT armor piece.
- **Wash with Soap**: a new interaction on the Copper Bathtub (Warm/Hot): drag vanilla Soap in for
  an extra-deep clean and a bigger mood boost. No new item, no new art.

## [1.15.9] - 2026-08-14

### Internal
- Marked the Copper Stove, Copper Brazier, and Metal Lantern (both lit and unlit variants of each)
  as compatible with the vanilla Firekeeping duty, so a recruited Partner NPC can be assigned to
  tend their fuel. Not yet verified in-game, so no player-facing behavior claim until confirmed.

## [1.15.8] - 2026-08-14

### Fixed
- **Powder ground at the Tea Station no longer vanishes when poured into bottles, cloth bags, or
  wooden barrels.** (Nexus bug report, 2026-08-10.) Powders such as Wheat Flour or medicine powder
  are solids with a pourable liquid form: the game stores their pour quantity on the card instance
  (`CurrentLiquidQuantity`, initialized to `SolidToLiquidInfo.LiquidQuantity` on a fresh card), and
  the built-in pour-into-container action always destroys the solid card and adds that quantity of
  liquid to the container. The Grind All in-place transform reset this quantity to 0, so pouring
  station-ground powder destroyed the card and added nothing; powder produced by a grinding disc
  or spawned directly was unaffected. Grind All now initializes the pour quantity exactly as
  vanilla card creation does. Note: powder ground BEFORE this update carries its zero quantity in
  the save file and will still vanish, so grind fresh material after updating.
- **Tin Ore can now be smelted in any smelting container, not only iron-capable ones.** Its
  `PassiveEffects` container gates referenced `tag_SmeltingContainerIron` instead of the general
  `tag_SmeltingContainer` used by every other smeltable item in the mod, and it was also missing
  the `tag_SmeltsAt1100` item tag those items carry. Corrected both: Tin Ore now behaves like the
  30+ other smeltable items in the mod and matches its own description ("Smelt it in a forge or
  furnace").

## [1.15.7] - 2026-08-09

### Documentation
- Documentation review for publish pass. Cross-checked README.md and ModInfo.json `Description`
  against shipped `CardData/`, `CharacterPerk/`, `WorldMap/`, and C# source for the 26 commits
  since the last public export (2026-07-17): both were already fully up to date (Salt Mine/Rock
  Quarry, bathtub Hot Bath tier, iron armor Male/Female split, Rendered Fish Oil, tin solder/nail
  cross-mod interchangeability, watering can and reservoir description corrections, Collapsed Rock
  Face "Always" seal fix). No content changes required. Version bump only.

## [1.15.6] - 2026-08-09

### Fixed
- **Collapsed Rock Face walls (Copper/Iron/Tin/Salt/Quarry cave passages) now spawn for every
  player by default, not only after equipping the Cave Prospector perk.** The perk was never
  actually required to dig through a wall (that's gated on tool tags, pickaxe/shovel/axe/antler/
  knife, on the wall's own `CardInteraction`); it only controlled whether the wall existed at all,
  which meant these passages were silently wide open on any save, including existing saves that
  install this mod without ever taking Cave Prospector. Requires `CSFFModFramework` 2.21.1+ (new
  `SealTrigger` type `"Always"`).

## [1.15.5] - 2026-08-07

### Fixed
- **5 stale Chinese translations re-synced to current English.** `Localization/SimpCn.csv` rows for
  `Bp_Handles` (gate item changed from Copper Sheet to Plank), the Building Materials and Metal Pan
  Tester starting perks (leather now specified as raw/untanned), the Large Saw help text (recipe now
  includes copper nails), and the Copper Brazier pack-up action (now preserves remaining oil) had
  drifted from their English source after earlier content edits. Caught by the fleet Chinese-parity
  checker's stale-anchor check (E23).

## [1.15.4] - 2026-08-07

### Added
- **Calming Tea, Focus Tea, and Warming Tea now carry `tag_Preservable`.** This is a vanilla CardTag
  with no shipped vanilla users; third-party mods that bulk-match spoilage-rate effects onto it (e.g.
  freshness/preservation perk mods) now apply correctly to our three perishable teas, which previously
  had no coverage.

## [1.15.3] - 2026-08-03

### Added
- **Iron-tier armor fastener slots now also accept Copper Nail and both WaterDrivenInfrastructure rivets** (previously only accepted Iron Nail): cross-tier fastener interchangeability is now bidirectional, matching the copper-tier slots' existing acceptance of Iron Nail/WDI rivets.
- **New Tin Solder ↔ WaterDrivenInfrastructure Alloy Solder interchangeability**: any blueprint requiring Tin Solder now also accepts WDI's Alloy Solder (soft dependency; no effect without WDI installed).

### Fixed
- **Copper Watering Can no longer advertises unimplemented interactions.** `CardDescription`, `CardHelpSection`, and README previously claimed it could "douse fires" and "pour into any water container", but neither interaction exists; the can only supports Fill from Water, Drink, and Empty Out. Text corrected to match.
- **Tea Blending Station's README description corrected**: the built-in reservoir is an 8-charge Water Charges tank, not a "300-capacity" reservoir; that figure belonged to the station's unrelated stove fuel stat and had bled into the wrong sentence.
- `Oil.json`'s card art reference corrected (`ClayBowl` → `Bowl_Clay`); it was pointing at a nonexistent sprite name and rendering blank.

### Changed
- Calming Tea, Focus Tea, and Warming Tea's spoilage stat is now labeled "Spoilage" instead of "Freshness," for consistency with the rest of the mod.

## [1.15.2] - 2026-07-28

### Changed
- **Rock Vein yield is now deterministic: 3 Stone + 3 Heavy Stone per strike, guaranteed, on both the Mine Stone (pickaxe) and Chip Away actions**, replacing the previous mixed guaranteed/chance drop table (2 Stone + 60% chance +1 + 50% chance Heavy Stone on Mine Stone; 1 Stone + 25% chance Heavy Stone on Chip Away). With the vein's existing 6-strike depletion count, this makes every Rock Vein worth exactly 18 Stone + 18 Heavy Stone regardless of which tool is used.

## [1.15.1] - 2026-07-28

### Changed
- **Collapsed Rock Face tunnels now drop 3 Heavy Stone (up from 1)** when cleared, alongside the existing 3 Stone; this applies to all six "Dig Through" actions (Copper, Tin, Iron, Salt, Salt↔Quarry, Quarry).
- **Rock Vein now also yields Heavy Stone**, matching the Collapsed Rock Face tunnels: 50% chance per pickaxe ("Mine Stone") strike, 25% per "Chip Away" strike. Rock Vein already shared the same 6-strike depletion count as every other vein; this brings its drop table in line with the tunnels too.

## [1.15.0] - 2026-07-28

### Added
- **Two new cave locations extend the mine network east.** A **Salt Mine** (3 × Salt Vein, dropping vanilla Salt) sits east of Copper Vein Cave, and a **Rock Quarry** (3 × Rock Vein, dropping vanilla Stone in bulk, 2–3 per pickaxe strike) sits east of Tin Vein Cave (Metal Mines). The two are connected to each other north–south (Rock Quarry south, Salt Mine north). Both new passages, and the link between them, use the same Collapsed Rock Face / Cave Prospector perk gating as the existing Tin/Copper/Iron network: open by default, sealed by the perk, one-time dig with a pickaxe, shovel, axe, antler, or knife to clear.

## [1.14.3] - 2026-07-27

### Fixed
- **Bathtub pick-up now preserves invested state.** Previously, picking up a filled or warmed placed bathtub silently discarded its firewood, water level, ash, and charcoal progress, and picking up a Warm tub degraded it straight to the cold Full kit instead of a matching Warm kit. The two portable kit variants (`copper_bathtub_full`, `copper_bathtub_warm`) now carry the same Firewood/Charcoal/Ash/Water-Level stats as their placed counterparts, and every kit↔placed transform (Place, Pick Up, Light Fire, and heat-depleted cooldown) transfers those values both directions. A picked-up Warm tub now stays a Warm kit; setting it back down restores it exactly as it was. Also corrected a pre-existing, previously-unflagged bug of the same class on the Empty variant's Pick Up (and everywhere else this shipped): the JSON key `TransferProgress` does not exist on the game's `CardStateChange` struct (only `TransferFuel`/`TransferCharges`/`TransferSpecial1-4` do); it silently deserializes to nothing, so charcoal progress was never actually transferred anywhere it was declared, including in the Warm bathtub's own pre-existing heat-depleted cooldown. All occurrences renamed to the real field, `TransferCharges`.
- **Iron Sheet's research-discovery gate now actually requires an iron-grade nugget.** It previously fired on any vanilla Metal Nugget regardless of metal type (the `UnlockConditionsDesc` said "Iron Nugget Needed" but nothing enforced it at the discovery stage; only the build-time recipe checked metal type). Now filters on `SpecialDurability4` 200 (iron), mirroring the already-correct Tin Solder pattern.
- **Collapsed Rock Face (Copper Vein Cave, Iron Vein Cave) no longer shows tin-vein art.** Both now use the vanilla `Tunnel_1` rockfall sprite already used by the Tin Vein Cave's own collapsed wall, matching what the passage actually looks like.

## [1.14.2] - 2026-07-27

### Changed
- **Small Copper Stove now has an NPC trading value (1500)**: it was the mod's one 0-cost
  tradeable item. Anchored to vanilla copper goods (Copper Bottle 800, Copper Jar 900). Part
  of the fleet-wide trading reprice.

## [1.14.1] - 2026-07-20

### Changed
- **Iron Armor now offers separate Male/Female torso variants** (`IronArmor_M.png` / `IronArmor_F.png`) instead of one unisex piece. Same Torso +45 protection and recipe as before: art only.

## [1.14.0] - 2026-07-17

### Fixed
- **Iron Sheet's card art**: it referenced the now-removed `IronSheet.png` sprite (replaced by the image-upgrade pass's `MetalSheet_Iron.png`) on both the item and its blueprint; it was silently falling back to a missing-sprite placeholder in-game.

### Added
- **Copper/Iron Sheet interchangeable with WaterDrivenInfrastructure's Cast Copper/Iron Sheet** (same tier only: Iron Sheet only pairs with Cast Iron Sheet) when WDI is installed. Nails were already cross-mod/cross-tier interchangeable with WDI's rivets (v1.11.5 and earlier); this closes the equivalent gap for sheets, and additionally makes Copper Nail accept WDI's new Iron Rivet. Soft dependency: no behavior change when WDI isn't installed.

### Removed
- **Copper Mattress-Frame Bed**: shipped in [1.12.0] (full kit/blueprint/placed CardData, sprite, and localization) and removed here: Community_Mod_Chest already ships the equivalent (`CopperBedFrame` → `Bp_ComfortableBed` → `ComfortableBed_Placed`), so this mod's own bed was redundant and will not return.

## [1.13.0] - 2026-07-16

### Added
- **Rendered Fish Oil blueprint** (Survival → Support tab): render 2 fatty fish meat + a clay bowl into the same Rendered Oil the fat and hemp recipes produce, a third lamp-fuel source for fishing playstyles. Unlocks when fatty fish meat is on board (16 ticks research).
- **Chamberpot waste → Manure**: the Copper-Rim Chamberpot's Empty action now leaves a pile of vanilla Manure, plugging the pot into the vanilla compost/fertilizer chain.

## [1.12.0] - 2026-07-14

### Added
- **Herbal Tea Beverages**: three brewable teas close the Tea Blending Station's long-standing "no output" gap: Calming Tea (dried willow bark), Warming Tea (dried wild garlic), and Focus Tea (dried spirit mushrooms). Brew any of them at the lit station once the reservoir is hot, consuming one Water Charge.
- **Bathtub Hot Bath tier**: the warm bathtub's bath action now splits into a lukewarm Warm Bath (5–50% heat) and a genuinely better Hot Bath (≥50% heat) with a bigger mood boost and a real Stress reduction, so keeping the fire stoked now matters mechanically, not just narratively.
- **Bronze-tier Oil Flask & Cauldron**: both items now carry a Metal Type stat and pick up copper/ghost bronze/tin/tin bronze/white bronze naming from whichever metal sheet built them, mirroring the existing Wearable Metal Pan pattern.
- **Copper Watering Can**: fills from any shallow water source; pours into water containers, douses fires, or serves as an emergency drink.
- **Copper-Rim Chamberpot**: a small hygiene convenience item: a minor mood bump for four uses before it needs emptying.
- **Copper Mattress-Frame Bed**: a kit → placed furniture piece with a passive comfort bonus while you're in its environment, better than sleeping on the ground. **Removed in [1.14.0]**: Community_Mod_Chest already ships the equivalent (`CopperBedFrame` → `Bp_ComfortableBed` → `ComfortableBed_Placed`), making this mod's own bed redundant.
- **Iron-Grade Armor Tier**: a new Iron Sheet (forged from iron nuggets) feeds an iron helmet/bracers/greaves/armor set with higher Armor Values and durability than the copper originals, so the Iron Vein Cave now has a genuine equipment payoff beyond nails.

## [1.11.6] - 2026-07-12
*(Covers changes since the last published release on 2026-06-23.)*

### Added
- **Cave Prospector perk**: optional 1-Star challenge perk that seals the cave network with Collapsed Rock Face barriers. Clear each barrier once to reopen the route and recover loose stone.
- **Wearable Metal Pan rain collection**: the wearable pan can now catch fresh rainwater during rain.

### Changed
- **Copper construction recipes now use tin solder more broadly**, adding a solder requirement to advanced copper builds such as armor, stove, cauldron, bathtub, brazier, kettle, oil flask, lantern oilwell, wheelbarrow parts, and the large saw.
- **Cave barriers accept more tools**: Collapsed Rock Faces can be cleared with a pickaxe, shovel, axe, antler, or knife instead of only pickaxe/shovel.
- **Tin Solder unlock and recipe now gate on tin nuggets** instead of the old raw Tin Ore requirement.
- **Iron Nails now gate on dried Bog Iron** and the ACT-only Iron Ore item was removed in favor of vanilla Bog Iron.
- **Tin Vein mining time now matches Copper and Iron Veins**, and iron/tin vein behavior has been normalized across cave nodes.

### Fixed
- **Iron Vein drops now spawn as usable vanilla Bog Iron with correct quality** instead of ACT Iron Ore or zero-quality output.
- **Cave Prospector gates now use framework sealable gates**, fixing portal bypass/stale travel-button behavior after digging through the walls.
- **Localization duplicate and truncation issues were cleaned up**, restoring corrupted Copper Stove, Tea Station, Copper Pantry, Metal Lantern, Large Saw, Metal Pan perk, and Chinese text rows.
- **Copper Stove help text now correctly lists charcoal as valid fuel.**

### Technical
- Nail interchangeability now delegates to `CSFFModFramework.Api.BlueprintAlternates` instead of a duplicated local implementation.
- ACT now uses the framework `ContentModPlugin` lifecycle for patch registration.

## [1.11.3] - 2026-07-04

### Changed
- **Tin Vein** mining times raised to match Copper Vein and Iron Vein: pickaxe 6→8, Chip Away 8→24. All three ore veins now take the same time to mine.

## [1.11.2] - 2026-07-04

### Fixed
- **Iron Vein** mining with a pickaxe cost 20 daytime ticks per strike (roughly 2 hours per attempt), far more than Copper Vein (8) or Tin Vein (6) for a comparable yield. Reduced to 8 to match Copper Vein.
- Bog iron mined directly from an Iron Vein now spawns at **50% Quality** instead of 0%. Vein-mined iron previously skipped the vanilla Fresh → Dried process (which normally raises Quality), leaving it at the lowest possible grade.

## [1.10.1] - 2026-06-21

*(Changes since v1.8.0, covering v1.9.0, v1.10.0, and v1.10.1)*

### Added
- **Iron Ore**: a raw ore chunk that smelts into an iron-grade metal nugget in a forge or furnace at 900°C. Heavier than copper ore and harder to refine.
- **Tin Ore**: a raw ore chunk that smelts into a tin-grade metal nugget at 900°C.
- **Iron Nail**: a hand-forged iron nail; stronger than copper and interchangeable with copper nails in all construction recipes.
- **Tin Solder**: a tin solder stick for joining metal parts or sealing seams.
- **Forge Iron Nails** (blueprint): convert iron-grade metal nuggets into iron nails in a forge.
- **Forge Tin Solder** (blueprint): melt a tin-grade metal nugget into solder sticks.
- **Copper Vein, Iron Vein, Tin Vein**: mineable ore veins found inside the new cave locations; use a pickaxe for best yield, or any digging/striking tool for slower extraction.
- **3 new world map locations**: Copper Vein Cave, Iron Vein Cave, and Tin Vein Cave: explorable cave areas each containing a dedicated ore vein.

### Changed
- **Building Materials** perk: cost reduced from 4 Moons to 2 Moons; kit description corrected ("small leather" instead of "leather").
- **Bathtub Kit** perk: cost reduced from 2 Moons to 1 Moon.
- **Large Saw** perk: converted to a drawback perk (45 Suns cost, was 3 Moons): the saw is powerful but you pay a survival penalty for starting with it.

### Fixed
- Iron Ore and Tin Ore now correctly produce **iron-grade** and **tin-grade** metal nuggets when smelted. Previously they defaulted to copper-grade, meaning they were only accepted by copper forge recipes and couldn't be used for iron tool blueprints.
- Ore cave veins now drop **vanilla Bog Iron** directly rather than a custom intermediate item, aligning with vanilla smelt chains and improving compatibility.

### Technical
- Saw effect logic migrated to the framework's Tier 2 ActionRouter API (no player-visible change).
- ⚠️ **All item UniqueIDs had underscores stripped** (e.g. `advanced_copper_tools_copper_nails` → `advancedcoppertoolscoppernails`). This is a **save-breaking change**: items from v1.8.0 saves will not be recognized after upgrading. Start a new run after updating.
