# Sirus23 Mod Collection

**Version:** 1.22.2
**Author:** Sirus, Jared
**For:** Card Survival: Fantasy Forest (EA 0.68)
**Requires:** CSFF Mod Framework 2.28.0 or later. Older frameworks back to 2.14.1 still load the mod, but the Wild Owl, the Hawk and the features marked "needs 2.28.0" below do not work fully.

**Language:** English, Simplified Chinese

Animal companions, a working sheep farm and felt clothing, by Sirus and Jared.

## Feature overview

| System | What it adds |
|---|---|
| Animal Companions | Wolf, Fox and Owl companions (perk or wild tame), Wolf Pup, Owlet, Hawk and Eyas, Ferret, Truffle Hog (with Herbs and Fungi), Recall Whistle, Wild Owl, Pack Bond, Wolf Den, Owl Perch, companion treats |
| Sheep Husbandry | Sheep, rams, lambs, milking, shearing, Tend Flock, feeding and seasons, Lanolin, dairy and dishes, Sheep Pen, Sheep Bell, Ox Plough, Ox Cart, trader stock |
| Felt Working | Felted Wool, Felt Hat, Mittens, Vest, Boots, Bedroll, Rug, Wool Socks, dyeing, set bonus; Feather Pillow |
| Perks | 9 character perks: Wolf Friend, Fox Friend, Owl Friend, Pack Bond (hidden, earned in play) and five sheep perks |

**Installation:** extract into `BepInEx/plugins/`, or install through Mod Update Manager. CSFF Mod Framework must also be installed. The mod has no config options.

**Compatibility:** Herbs and Fungi, Partner Overhaul, Harsh Wilds and Far Territory each add something when installed (see Working with Other Mods). Without them nothing changes.

**Status notes:** a number called a first guess may change after playtesting. "Not yet confirmed in the game" means no one has checked that feature in a running game.

## Character-creation perks

All eight are in the Situational tab.

| Perk | You start with |
|---|---|
| Wolf Friend | a Wolf Companion |
| Fox Friend | a Fox Companion |
| Owl Friend | an Owl Companion |
| Start with Sheep | one Tame Sheep |
| Start with Shears | Shears |
| Sheep Husbandry Kit | a Ram, a Lactating Sheep and a Lamb |
| Shepherd's Start | two Tame Sheep, a Ram, a Lactating Sheep and Shears |
| Shepherd's Crafting Kit | 4 Wool, 2 Warm Milk, a Butter Churn and 2 Cheese Cloths |

The ninth perk, Pack Bond, is hidden. Only the wolf's Bond with Wolf action grants it.

## Animal Companions

### Upkeep

Every companion has Hunger, Thirst and Morale bars. Drag food or water onto it to feed it, and pet or play with it for Morale. A neglected companion dies or leaves.

Below 20% Hunger or Thirst, a companion eats or drinks from the nearest source on the board. It drinks only water, never brine, tea, milk or medicine, and Give Water only appears for a container of water. Any mod's container counts if it is tagged as a water container and holds water.

### Health and Treat Wounds

The wolf, fox, owl and hawk each have a Health bar. The wolf's falls while its Hunger is below a tenth and slowly mends while Hunger is above 60%.

The fox's, owl's and hawk's falls only while a bar is empty, by 2 points every 15 minutes per empty bar. A full 480 lasts about two and a half days with one bar empty, half that with both. At zero the wolf and fox die and leave a body, and the owl and hawk fly away.

**Treat Wounds** restores 192 Health. Drag Healer's Moss, Boiled Moss, Healing Ointment, Healing Salve, Pine Salve, Soothing Ointment, Soothing Salve, River Poultice or Lanolin Salve onto the companion. The Lanolin Salve is not yet confirmed in the game.

A fox or owl from an older save loads with full Health. The 480, the drain and the 192 are the wolf's numbers and a first guess.

### Wolf Companion

The Wolf Friend perk starts you with a wolf. Feed, water, treat, pet and play with it, send it hunting, have it Track Scents, Flush Prey for a piece of Meat, or set it to Guard Camp.

While the wolf shares your location, encounters that start with no animal or person on your board stay away. An animal or person who walks onto your board and attacks still can, and a fight you start is never stopped.

**Bond with Wolf** is a one-time, two-hour action. It grants the hidden Pack Bond perk: +15 Tracking.

**Taming a wild wolf.** Use Attempt to Tame on the lone wild wolf with Meat, Bird Meat, Lean or Fatty Fish Meat, Dried Meat or Dried Bird Meat. It becomes a Wolf Companion with full Hunger, Thirst and Morale, and the wild wolf leaves. A wolf pack or the primeval wolf cannot be tamed this way.

**Wolf Pup.** Offer meat to a wolf pack and you win a Wolf Pup. Feed Pup and Give Water speed its Growth bar, or you can wait. When the bar runs out it becomes a full Wolf Companion.

Only one wolf or pup follows you at a time, so the wild wolf and the pack offer no tame while you have one.

### Fox Companion

The Fox Friend perk starts you with a fox. It eats meat, bird meat, fish and bones, takes dried-berry treats, and can be watered, treated, petted and played with. Its Morale drains more slowly than the wolf's.

**Scout Ahead** (45 minutes) sharpens your Tracking and brings back 1 or 2 raw meat. **Retrieve Partridge** is a timed hunt. While the fox is with you, it stops the same encounters as the wolf about one time in three.

**Taming a wild fox.** Offer an ordinary wild fox Springberries, Billberries or Juniper Berries, fresh or dried.

**The fox scouts the way.** While the fox follows you, every leg of a journey takes 15 minutes less, though an exit with a minimum time keeps it. A fox told to Stay Here gives no bonus.

### Owl Companion

The Owl Friend perk starts you with an owl. It eats meat and bird meat, takes dried-meat treats, and can be watered, treated and petted. Its Hunger and Thirst drain more slowly than the wolf's or fox's.

**Night Watch** is a one-hour rest that restores sleep and reduces stress. **Retrieve Mouse** brings back a Dead Mouse. While the owl is with you, it stops the same encounters as the fox, about one time in three.

The owl keeps night hours, 20:00 to 06:00. At dawn it flies to the nearest natural cave to roost. At dusk, if you are outdoors, it returns with its stats as they were. By day it does not travel with you, and it never follows you into a cave or building. An owl told to Stay Here is left alone.

If its Health runs out, the owl flies away and leaves no body.

### The Wild Owl

A Wild Owl comes out at night (20:00 to 06:00), drawn to your camp's light. Where it last visited it leaves Owl Pellets & Feathers, which your Tracking skill helps you spot next morning.

Approach it and it may flee or fight. Offer a mouse, a minnow or a dead partridge to tame it: about 50% untrained, rising to about 75% at 150 Tracking. A failed tame has a small chance to make it attack. A success gives the same Owl Companion as the perk, and the wild owl is gone for good. You can also catch it alive in a baited snare.

Between 22:00 and 04:00 it may attack you on its own, at most once a night. While you have an Owl Companion it stays away, and it returns some time after the companion is lost, later still if you killed or caught it.

### Owlets and Owl Pellets

A successful Climb at a bird's nest finds an Owlet about one time in twenty. Feed it meat and water and it grows into an Owl Companion. No owlet turns up while you have an Owl Companion or an Owlet.

**Pick Through the Pellets** on Owl Pellets & Feathers gives one or two Feathers and Bone Pieces, and uses the card up.

### Companions Keep Each Other Company

When two companions share a location, each one's Morale falls a quarter more slowly. The bonus applies once however many share, and it stacks with the Wolf Den and Owl Perch.

The Hawk Companion's Morale also falls a quarter more slowly beside the wolf or fox, though they gain nothing from the hawk. The hawk's part is not yet confirmed in the game.

### The Hunting Hawk

A wild Hawk roams oak and pine clearings and oak groves from 07:00 to 19:00 and never attacks. Drag a mouse, a squirrel or a Partridge Carcass onto it to Attempt to Tame. Tracking raises the chance, as with the owl.

In spring a successful Climb at a bird's nest can also find an Eyas, at the owlet's odds. Raise it like the Wolf Pup. Either way you get a Hawk Companion, and only one hawk at a time.

The Hawk Companion follows you, with Stay Here and Follow Me as for the fox. **Send Hawk Hunting** (1 hour) brings back a Hare Carcass or Partridge Carcass about 45 times in 100, half the wolf's rate.

That rate is a first guess until Sirus reviews it. The Eyas uses the Owlet's picture for now. Not yet confirmed in the game.

### The Recall Whistle

A companion told to Stay Here, or left where it could not follow, waits where it is. The Recall Whistle fetches it. Craft it from one Bone Pieces (Farming > Animal Husbandry, shown once you hold Bone Pieces).

**Blow the Whistle** (15 minutes) works anywhere. Every Wolf, Fox, Owl or Hawk Companion in another location comes to you with its stats as they were and follows you again.

The owl answers only at night and only when you are outdoors. At night the whistle also brings an owl told to Stay Here. If nothing answers, the card says so, and a companion that fails to appear is put back where it was. Not yet confirmed in the game.

### Wolf Den and Owl Perch

Two camp structures under Farming > Animal Husbandry. Each is a kit: Place sets it up and Pick Up packs it away.

| Structure | Recipe | Blueprint appears once |
|---|---|---|
| Wolf Den | 4 Planks, 4 Long Sticks, 2 Wool and a hammering tool | your Wolf Companion has been on your board |
| Owl Perch | 3 Long Sticks and 2 Rope | your Owl Companion has been on your board |

While the wolf shares a location with its den, or the owl with its perch, its Hunger and Morale drain a quarter more slowly. A companion that travels with you leaves the bonus behind. A second den or perch in one place adds its own quarter, so build one per companion. There is no fox den.

The quarter, the recipes and the build times are first guesses. The slowdown needs CSFF Mod Framework 2.28.0 or later to wear off cleanly when the companion leaves.

### Companion Treats

Each companion has a craftable treat under Survival > Cooking. Each recipe makes two:

| Treat | Recipe | For |
|---|---|---|
| Bone Treat | 1 Bones and 1 Dried Meat | wolf |
| Owl Treat | 1 Meat and 1 Salt | owl |
| Berry Mix | 1 Dried Springberries and 1 Dried Billberries | fox |

A crafted treat builds twice the bond a raw treat does, gives you a small Morale lift and eases Loneliness. The values are first guesses.

## Small Animals in Your Pack

### The Ferret

The Ferret rides in your pack. Carried in the same location as a trap, it adds 20 points to a Snare's chance of catching a hare and 10 to a Log Trap's. Deadfalls and Pit Traps cannot catch hares.

Its Hunger bar empties over about ten days. Drag Meat or Bird Meat onto it (raw, cooked, boiled or dried) to **Feed Ferret**. A hungry ferret helps less, and a starving one not at all.

You cannot find one in the wild. The animal trader sometimes stocks one on spring and autumn visits (a 25% chance). The bonuses and the 25% are first guesses. Not yet confirmed in the game.

### The Truffle Hog (needs Herbs and Fungi)

With Herbs and Fungi installed, drag Edible Acorns onto a Piglet whose Trust is at least 95% and choose **Train to Root** (1 hour). The piglet becomes a Truffle Hog. Without Herbs and Fungi the piglet never gets the action.

The hog rides in your pack and its Hunger bar empties over about ten days. Refill it with an acorn or a root: Pine Roots, Cattail Root, Turnroot or Fireroot.

Carried and fed, it adds 2 points to the rare truffle find of Dig up Mud and Dig up Dirt (still nothing out of season) and 15 points to Dig for Truffles on oak and alder ground. A hungry hog helps less. Both bonuses are first guesses. Not yet confirmed in the game.

## Sheep Husbandry

Sheep, rams and lambs, with milking, shearing and the structures to keep them. Milk becomes curdled milk, cream, butter, buttermilk, yogurt, ricotta, sheep cheese and aged sheep cheese. Wool becomes lanolin, wool yarn, woven cloth, wool clothing and bedding.

### Getting Sheep from the Trader

The travelling animal trader at Green Glade can bring a Tame Sheep (a ewe) and a Ram on spring and autumn visits. Each has its own 50% chance, so a visit offers neither, one or both. That lets you start a breeding pair without a sheep perk. The ewe's trade value is 3000 and the ram's 2500. The 50% is a first guess. Not yet confirmed in the game.

### Shearing and Lanolin

Tame sheep, lactating sheep and rams can all be sheared. Drag Shears onto one whose Wool bar is at least half full for 2 Wool. The bar is ready again in about two and a half days. A ram's fleece grows like a ewe's (a first guess), and a ram from an older save loads with a full fleece. Shearing a ram is not yet confirmed in the game.

Shears last 50 shearings, then wear out.

Each shearing has a 25% chance to give **Lanolin**. Blend it with Frostleaf Powder into a **Lanolin Salve** (Survival > Medical, shown once you have Lanolin) and rub it on for a lasting Comfort boost. The salve does not spoil.

### Tend Flock

The Sheep Feeder's **Tend Flock** shears every ready tame sheep, lactating sheep and ram in your location in one click, each for the same 2 Wool and 25% Lanolin chance.

First it feeds the flock. Any sheep below two thirds Nourishment eats one feed card from the feeder (grass, hay or any herbivore feed) and gains 144. When the feeder is empty it uses a base-game Dry Fodder pile in the same location, one unit per sheep. A pile below one unit is left for cattle.

Tend Flock does not milk. Drag a container onto a Lactating Sheep to milk her by hand.

### Well-Fed Flock and Seasons

Tame sheep, lactating sheep and rams have a Nourishment bar of 288 that falls 1 point every 15 minutes, so a full bar lasts three days. Drag grass, hay or any herbivore feed onto a sheep to **Feed** it (+144). A sheep that is nearly full says "This sheep is not hungry".

At 96 or more a sheep is well fed: wool regrows 50% faster and a lactating sheep's milk comes 50% faster. A hungry sheep never starves and produces at the normal rate. A ewe keeps her Nourishment when she gives birth, sheep from older saves load with a full bar, and lambs that have just grown up may start empty.

In spring wool regrows 25% faster. In winter milk comes 25% slower and Nourishment drains twice as fast, unless the sheep is in a Sheep Pen, even one with a worn fence. The bonuses multiply, so a well-fed sheep in spring regrows wool 87.5% faster.

The base game's grazing bonus no longer reaches sheep. It raises Happiness, which sheep lack, and on a Lactating Sheep it refilled her milk in just over an hour instead of about six days.

Every number here is a first guess. The wool and milk bonuses need CSFF Mod Framework 2.28.0 or later to wear off cleanly when the season or feeding changes.

### Manure, Twins and the Loom

Tame sheep, lactating sheep and rams each drop a base-game **Manure** about every four days for the compost bin. A sheep from a save older than 1.22.0 starts counting from zero.

A pregnant ewe has **twins** 15% of the time ("Twin lambs!"), otherwise one lamb. The 15% is a first guess.

The sheep's yarn is called **Wool Yarn**, so it is not confused with the base game's Yarn. Any base-game loom recipe that takes Yarn also takes Wool Yarn, and the cloth comes out at base quality. Sirus's recipes still need Wool Yarn.

### Washing Wool

Drag a filled water container onto raw Wool and choose **Wash** (15 minutes, 100 water). Washed Wool spins into two Wool Yarn instead of one, using the Wool Yarn from Washed Wool blueprint. Raw wool still spins and felts as before. Washed Wool uses Wool's picture for now.

### Ox Plough

The **Ox Plough** (Farming > Agriculture, 4 Planks and 2 Rope) appears once a base-game Bull has been on your board. With a Bull on the board, drag it onto a Fallow Field or Tilled Field to **Plough the Field**. In half an hour the field is tilled exactly as the hoe leaves it, and the Bull gets hungrier. The plough lasts 50 fields. The stamina cost, hunger and durability are first guesses.

### Ox Cart

The **Ox Cart** (Survival > Support, 8 Planks and 4 Rope, 2 hours to build) appears once a Bull has been on your board. In your hand it works like the Travois but carries 8000 instead of 4000.

With a Bull on the board beside you, the Travois's extra travel time and effort is lifted. A Bull loose beside a hand-held cart lifts it for the first leg only, then stays behind.

Hauling wears the cart down. Drag a Plank onto it to **Repair** it. At zero it falls apart into two or three oak Planks and a Rope. The capacity, durability, repair and cost are first guesses.

### Slaughtering a Sheep in Full Fleece

Slaughter a Tame Sheep, Lactating Sheep or Ram as usual and its Wool bar decides the pelt. At half full or more you get the usual meat, fat, bones and offal plus a base-game **Fresh Fur** instead of the Fresh Skin. Below half, the sheep counts as shorn: a Fresh Skin, plus loose Wool from a Tame Sheep.

A Tame Sheep in full fleece drops no loose Wool, because the pelt carries it, so shear her first. The pelt is a small fur, in the range of a fox pelt and below a wolf's, so it scrapes into a small fleshed skin for base-game fur clothing of that size, such as Fur Mittens. Its Fur and Quality are first guesses.

### Lanolin Instead of Fat

**Lanolin** can replace **Fat** in six base-game recipes: the Healing Salve, Pine Salve, Soothing Salve, Bug Repellent Salve, Candle and Soap. Every other recipe that takes Fat, including Dynamite and the Fat Chunk, still takes Fat only.

This needs CSFF Mod Framework 2.28.0 or later. On an older framework the six recipes take Fat only and the rest of the mod works.

### Salvaging Sheep Remains

A sheep taken by a predator leaves **Sheep Remains**. Drag a cutting tool onto it for **Salvage Remains** (45 minutes): 2 Wool, a Fresh Skin, 2 Meat and Bones. The yields are a first guess.

### Sheep Pen

The **Sheep Pen** (Farming > Animal Husbandry, 8 Planks, 4 Rope and 4 Stone, plus a hammering tool that is not used up) holds up to four tame sheep and rams. Inside, wool, milk and breeding carry on as normal. Pick Up puts the sheep on the ground before packing the pen.

**Pen or perish.** Each night, every tame sheep, ram or lactating sheep outside a pen may wander off (4%) or be taken by a predator (6%). A kill where you stand leaves Sheep Remains. A **Wolf Companion** in that location cuts the predator chance to 1% but does not stop wandering. Only a pen keeps a flock fully safe. The 4% and 6% are cautious first guesses.

**The base game's Enclosure counts as a pen.** Drag sheep onto its entrance. They roll nothing overnight, with or without a wolf, and stay safe while you are away.

**Fence Condition.** A placed Sheep Pen's fence wears down over about twenty days. At zero the sheep stay inside but roll each night like loose sheep, and the log says the fence is worn. Drag a **Plank** onto the pen to **Mend the Fence** and restore half the bar. Packing and placing the pen does not mend it, and a pen from an older save loads at full. The twenty days and the half are first guesses.

**Sheep Bell.** A bell on a rope collar: 1 Metal Nugget and 1 Rope (Farming > Animal Husbandry, shown once a Tame Sheep has been on your board). Unpenned sheep and rams in its location are half as likely to wander off (2% instead of 4%). It does nothing against predators and works only in its own location.

**Predator Tracks.** A predator kill also leaves **Predator Tracks**. **Follow the Tracks** (half an hour) starts the base game's wolf fight, and a win gives only the fight's own rewards. The trail fades after about a day. With your Wolf Companion on the board the button is hidden, since the wolf keeps predators away. With a Fox or Owl Companion the predator is scared off about one time in three.

**Orphan lambs.** When a predator takes a Lactating Sheep, half the time her lamb is left beside the remains: a newborn Lamb with a full Growth bar (the full bar needs CSFF Mod Framework 2.28.0 or later). The 50% is a first guess.

**A flock you leave behind is still at risk.** Only sheep in your hands travel with you. Sheep left loose roll every night to wander off or be taken, as if you were there. A Wolf Companion told to Stay Here with them guards them (1% predator chance), and a Sheep Bell left with them halves the wandering.

A loss while you are away leaves no remains, tracks or lamb, since the game can only place cards where you stand. You come back to a smaller flock, and the log names each loss. Reloading neither rolls a night again nor brings a sheep back. The bell and Enclosure parts are not yet confirmed in the game.

## Dairy

### Prepared Dairy Dishes

Drag a dairy product onto the base-game food below. Both are used up and you get the new dish.

| Drag | Onto | Makes |
|---|---|---|
| Butter | Roasted Cattail Root | Buttered Roots |
| Sheep Cheese | Wheat Roundbread | Cheese-Stuffed Flatbread |
| Cream | Mashed Turnroot | Creamy Mash |
| Yogurt | Dried Springberries | Berry Yogurt |
| Sour Cream | Boiled Turnroot | Soured Turnroot |
| Ricotta | Rye Roundbread | Ricotta Rye Round |
| Buttermilk | Wheat Hardtack | Buttermilk Hardtack |

Each dish is more filling than either ingredient alone. The nutrition values and pairings are first guesses.

### Dairy Preservation

Drag **Salt** onto Butter to **Salt the Butter**: Salted Butter keeps three times as long. Drag any fire source onto Sheep Cheese to **Smoke the Cheese**: Smoked Cheese keeps four times as long. Both use up the fresh product. The multipliers are first guesses.

### Cooking with Butter

Sheep Butter, Salted Butter and Clarified Butter all take the base-game butter's three **Add** actions. With Wheat Dough or Rye Dough they make Wheat or Rye Grease Dough. Dragged onto Raw Minced Meat, Blood Curd, Raw Fatty Fish Meat or Raw Lean Fish Meat, they make the Raw Meat, Raw Blood or Raw Fish Filling.

They also melt like base-game butter in a Clay Bowl, Clay Cauldron or Cooking Pot that already holds some melted butter, becoming fresh liquid Butter at half Quality. Melting is not yet confirmed in the game.

### Dairy Flavours

Sheep Cheese has the flavours of the base game's Firm Cheese Wedge. Aged Sheep Cheese and Smoked Cheese have the Aged Cheese Wedge's, and Smoked Cheese adds a Smokey note. Eating one counts toward your flavour score. Butter, Yogurt and Sour Cream have none, like base-game butter.

A dairy dish keeps the flavours of the food it was made from: Cheese-Stuffed Flatbread tastes of its Wheat Roundbread. Cheese from an older save has no flavours; newly made cheese does. The Smokey strength is a first guess.

### Herbs in Dairy

Drag wild garlic, wood sorrel, clover, nettle leaf, frostleaf or juniper berries onto **Sheep Cheese** or **Butter** to stir them in. The herb is used up and the dairy takes on its flavours. Strong herbs add up, as on the base game's pie. Herbs and Fungi is not needed. Not yet confirmed in the game.

### Straining Curds by Hand

Drag the Cheese Cloth onto **Curdled Milk** to **Strain Curds** (45 minutes) with no fire. It is slower than Press Cheese at a fire, and the whey drains away.

## Felt Working

Felt is the alternative to spinning and weaving. Press 4 raw Wool into a sheet of **Felted Wool**, then make:

| Item | Slot | Cold Resistance | Notes |
|---|---|---|---|
| Felt Hat | head | +8 | |
| Felt Mittens | hands | +5 | |
| Felt Vest | outer torso | +12 | shares the slot with the Wool Tunic |
| Felt Boots | outer shoes | +8 | also needs Twine |
| Wool Socks | inner shoes | +5 | knitted from 2 Wool Yarn |

The **Felt Bedroll** (3 Felted Wool) has the base-game Bedroll's Nap and Sleep actions. As bedding it is warmer than the Wool Blanket: +16 Body Temperature against +10, at 900 g against 700 g. Sleeping on it is not yet confirmed in the game.

The **Felt Rug** (3 Felted Wool, shown once you have Felted Wool) is a flat rug with a darker border. It gives +10 Comfort and weighs 1000 g.

All eight blueprints are in Tailoring > Cloth. The Mittens, Vest and Bedroll numbers, the set bonus and the rug's stats are first guesses.

**Dyeing.** Put a Felt Hat, Mittens, Vest or Boots into a Clay Basin, Rain Cistern or Tanning Pit holding a base-game dye. The cloth dye recipes run on it and rename it for the colour (Green Felt Hat, Red Felt Vest). A dyed piece gains a Colour Intensity bar that fades over a couple of days, sooner with Lye or Urine in the station. Dye changes only the name: warmth stays the same, and the cloth shirt's colour bonuses do not carry over. The Felt Bedroll, Felt Rug and Wool Socks cannot be dyed.

**Set bonus.** Wear three or more of the Felt Hat, Mittens, Vest and Boots for **+4 Cold Resistance** on top of their own. Four pieces give the same +4 as three. The Wool Tunic and Wool Socks do not count.

## Feather Pillow

Processing an Owl Carcass or a Hawk Carcass gives six Feathers. The **Feather Pillow** (Tailoring > Cloth, shown once you have Feathers) takes 1 Woven Cloth, 12 Feathers and 4 Twine. It gives **+10 Comfort** while carried, a first guess.

## Working with Other Mods

- **Herbs and Fungi:** enables the Truffle Hog. Ground yarrow or dried chamomile can also replace Frostleaf Powder in the Lanolin Salve, and only there (needs CSFF Mod Framework 2.28.0 or later).
- **Partner Overhaul:** a partner can eat Sheep Cheese, Yogurt, Berry Yogurt, Ricotta and most cooked dairy dishes (not Aged Sheep Cheese, Smoked Cheese or Soured Turnroot).
- **Harsh Wilds:** after you see a black bear or eagle raid, unpenned sheep in that location face double the predator chance that night and the next (12% instead of 6%, or 2% instead of 1% with the wolf on guard). The lynx does not count, and reloading forgets a raid. The doubling is a first guess.
- **Far Territory:** the Wild Owl never lands on its islands.

## For Modders

**Contents of this build:** 9 perks, 71 item cards, 1 liquid, 7 location cards (Butter Churn, Sheep Feeder, Sheep Pen, Wolf Den, Owl Perch, Predator Tracks, Owl Pellets & Feathers), 29 blueprints, 2 spawn triggers (wild sheep and wild ram) and 3 encounter guards (wolf, fox, owl). It also has 2 declarative animals (Owl and Hawk), 1 NPC agent (Wild Owl) with 5 NPC stats, 2 game stats (felt outfit pieces, fox scout pace) and 4 `GameSourceModify` patches adding Attempt to Tame to the vanilla wolf, wolf pack and both fox agents.

**Hooks for other mods:** Treat Wounds accepts any card tagged `tag_x_AnimalRemedy`. The Partner Overhaul dishes carry the vanilla Partner eat duties and `tag_HumanFood`.

**Structure:** content loads through CSFFModFramework. Blueprints are mapped in `BlueprintTabs.json` (no `ScriptableObject/CardTabGroup/` overrides), perks load from `CharacterPerk/`, wild sheep spawn from `CardData/Trigger/`, and runtime behaviour lives in focused Harmony patches.

**Dependencies:** `CSFFModFramework` only, as a `SoftDependency` for load order. No other in-house content mod depends on this one, or it on them.

**Building:** the project targets .NET Framework 4.8. Its csproj `HintPath`s name `../HerbsAndFungi/lib/`, but MSBuild resolves references from the project's own folder first, so the build binds this mod's own `lib/` copies. Refresh `lib/Assembly-CSharp.dll` and `lib/CSFFModFramework.dll` here after a game or framework update. `Development_Tools/Deploy-Mods.ps1 -CSFFModFramework -Sirus23ModCollection` builds and deploys both, keeping the trigger JSON subfolder the framework needs.

## Credits

Authors: Sirus and Jared. Built on CSFF Mod Framework by crispywhips. See `LICENSE` for licence terms.
