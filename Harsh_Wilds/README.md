# Harsh Wilds

**Version:** 0.11.7
**Author:** Jared
**Requires:** CSFFModFramework 2.28.0 or later
**Status:** pre-release. Rewards (meat, affinity, coats, rites) were tuned to their base-game equivalents in 0.11.6; the threats' chances, numbers and lengths are still first guesses not yet balanced against play. Where a feature is not yet confirmed in a running game, its section says so.

Harsh Wilds makes the forest harder and older. It adds optional threats for players who find the base game's wilderness too tame, plus the forest's old powers: spirits to call, rites and brews, rare beasts, the fair folk, and realms beyond the forest.

Every threat has its own switch in the config file. Things you do on purpose (spirits, rites, brews, ritual objects, the realms) have none.

## What is in it

| Area | Contents |
|---|---|
| Creatures | Lynx, wisent, musk ox, bandit |
| Rare beasts | White wisent, lake beast, frost demon, lindwurm |
| Raids | Eagle, black bear, wolverine, heron (needs Water-Driven Infrastructure) |
| Hazards | Adder, smithing burns, foraging bites, mud volcano (needs Forest Lore) |
| World events | Drought, insect plague, crop blight, blood moon, eclipse, aurora, cold snap, algae bloom |
| Afflictions | Fleas and lice, tooth decay, spirit sickness, dancing plague |
| Spirit path | Lynx, Goat, Blood, Dragon and Ancestor Spirits; rites; brews; witchfire; ritual objects |
| Fair folk | Banshee, nixie, Wild Hunt, selkie (needs Far Territory), Threshold Stone |
| Places | Fairy ring and three realms: Faerie, Moon and Underworld |
| Visitor | A wandering shaman who trades once a season |

## Creatures

### Lynx
A big cat of the rocky pine forests (Rocky Pines, Mountain Grove, Mountain Thicket, Cliff Thicket).

- It roams from dusk to dawn and hides by day. Its tracks are hard to find until your Tracking is high.
- If you are within a couple of paths of it at night, it comes for you and attacks at most once a night. You can also approach it and start the fight yourself.
- Its bites and claws leave bite wounds and claw marks.
- It leaves a Lynx Carcass to butcher with a knife for fur, meat, fat and bones. A killed lynx is replaced after about a week.
- A baited log trap set in its area can catch it, leaving a Trapped Lynx you butcher the same way. Deadfalls, snares and pit traps do not work on it. The two blood moon lynxes cannot be trapped.
- On a new run it stays out of the world for the first week, as the base game holds back its wolves. A game already past its first week sees no change.

### Wisent
A huge wild ox of the oak glades (Green Glade, Mossy Clearing, Green Grove, Mysterious Grove, Oaken Grove).

- It grazes day and night and never attacks first. Its tracks are easy to find.
- If you fight it, it knocks you down, rams you and gores you. Expect bruises, sprains, cuts, puncture wounds and broken bones.
- It leaves a Wisent Carcass: bleed it, skin it with a knife, then butcher it in four cuts for a great deal of meat and fat, organs, tendons, bones and two horns.
- A killed wisent is replaced after about two weeks.

### Musk ox
A shaggy ox that visits the northern clearings (Pine Meadows, Still Hollow, Moon Hills, Badger Hill) in winter only.

- There are three. Each roams on its own, so you may meet one, two or all three.
- It never attacks first. Fighting it means facing the herd's ring: it is hard to surprise, hard to hit from a distance and slow to give up.
- Its carcass is bleed, skin for a Large Fresh Fur, then four cuts for meat (about 62, a stag's and a third), fat, organs, tendons, bones and two horns.
- A killed musk ox is replaced after about 30 days, in winter. The other two stay.

### Bandit
A ragged spearman who waylays travellers on the river (Southern Rapids, Rocky Bank, Northern Rapids, River Pass). He appears nowhere else.

- No bandit exists in the first ten days of a run. The first one turns up about a day after that.
- He is out from 06:00 to 20:00. If you share his place between 07:00 and 19:00, he ambushes you at most once a day, once your current action ends. You can start the fight yourself with **Confront**. The fight opens with a choice, and "Back away" costs nothing.
- It is a spear fight: abrasions, cuts and puncture wounds, and more fear and bruising. The worse shape you are in, the likelier he knocks you down and robs you. A badly hurt character can still die.
- Win and he leaves a Fallen Bandit. "Search the body" pays one of three small hauls, then the body is gone. A killed bandit is replaced after about a week.
- Escape, back away or drive him off and it costs nothing.
- Lose and he takes up to three pieces of travel food from your hand row and from bags and baskets you carry. A "Robbed by a Bandit" popup lists what he took. He can only take: Dried Blood Sausage, Dried Rich Blood Sausage, Billberry Fat Cake, Springberry Fat Cake, Hard Cheese Wedge, Aged Cheese Wedge, Meat Cake.
- He never takes tools, worn items, anything a recipe or task needs, or anything in a store or chest you set down. With none of the seven on you, he takes nothing.

## Rare beasts

Each comes only on rare days or nights, announces nothing, and is not replaced on a timer: the next one comes when a later day brings one. Each has its own switch.

- **White wisent** (`WhiteWisent`, also needs `Wisent`). About one day in forty, at noon, a rare albino wisent comes to the wisent's glades for about four days. It never attacks first. Its fight is the wisent's with a stouter hide. Its carcass is butchered like the wisent's, and **Release the Spirit** on it gives Beast Affinity +100, where other wild carcasses give +25. It uses the cow's picture for now.
- **Lake beast** (`LakeBeast`). About one day in sixty, one comes into the lake and its banks (Northern and Southern Lake Bank, Lake Grove) for about three days. At night, 20:00 to 05:00, it can come for you on a bank, once a night, and fights about as hard as a bear. You can also approach it. Its carcass is bleed, skin, four cuts.
- **Frost demon** (`FrostDemon`). On about one winter evening in fifteen, one comes down to the northern hills and meadows at 18:00 and stays until about 05:00. Out of doors there it can hunt you once a night. Its bites are Frostnip and Frostbite wounds. A roof is the way out. Its carcass is butchered in one go for meat, fat, bones and a white fur.
- **Lindwurm** (`Lindwurm`). About one day in a hundred and twenty, a great serpent takes up its lair in the Great Cave for about a week and a half and never leaves it. If you are in the cave, it can come for you once a day at any hour, and you can approach it yourself. It fights harder than the lake beast. Its carcass is a heavy single butchering (about 150 meat, about what a bear gives, plus fat, bones and a Very Large Fresh Skin) and holds a spirit: see the Dragon Spirit below.

## Raids

A raid is a card, not an animal. You cannot fight it or butcher it. The card appears, and its **See what it takes** button shows what was carried off. The raid takes exactly one card per visit.

If a raid comes while you sleep or nap outdoors, your sleep ends one quarter hour later so you wake to its card. This covers the black bear and the eagle. The wolverine and the heron do not wake you. A lynx that arrives while you sleep starts its fight, and the fight ends the sleep.

### Eagle (`Eagle`)
Swoops on small game lying loose on the ground.

- It comes only while you are outdoors, at most once every two days, and stays away for about the first two weeks after the mod is added.
- It comes only if there is something for it: the more loose small game in your place, the likelier it is. None, no visit.
- A tame dog (the Dog Friend card) lowers the odds a good deal. With Sirus23's Mod Collection, so does a tamed wolf or fox.
- Spilled blood draws it, fading over about two weeks. Heavy or Torrential Rain grounds it. Fights and livestock raise the blood; butchering does not.
- It can take: Hare Carcass, Dead Partridge, Dead Duck, Dead Squirrel, Dead Mouse, Frog, Bird Meat, Partridge, Male Partridge, Trout, Perch, Pike, Char or Crayfish. With Sirus23's Mod Collection it also takes an Owl Carcass or a Snared Owl.
- Anything in a container, a trap, your hands or an equipment slot is safe.

### Black bear (`BlackBear`)
Finds the meat, fish and sausage in your food stores.

- Autumn only. The first five days of each autumn are quiet. After that, at most once every three days, and only while you are outdoors.
- The odds rise with every piece of meat, fish or sausage kept in a store or container in your place (drying rack, barrel, shelf, placed basket or sack). Loose food on the ground does not count.
- A tame dog lowers the odds a good deal. A burning campfire or fire pit lowers them by half as much. With Sirus23's Mod Collection a tamed wolf counts like the dog.
- It can take: Meat, Dried Meat, Dried Bird Meat, Raw or Dried Lean Fish Meat, Raw or Dried Fatty Fish Meat, Dried Sausage, Dried Rich Sausage, Dried Blood Sausage, Dried Rich Blood Sausage or Honeycomb. With Advanced Copper Tools it also takes Salt-Cured Meat. With Sirus23's Mod Collection it takes Sheep Cheese, Aged Sheep Cheese or Smoked Cheese.
- It reaches only stores standing in the place (rack, barrel, shelf, placed basket or Copper Chest). Food in a basket set down on the ground still raises the odds but is out of reach, so that visit can find nothing and end with no message.

### Wolverine (`Wolverine`)
Robs your trapline in winter.

- Winter only, at most once every three days.
- The odds rise with every sprung trap (Snare, Deadfall, Log Trap or Pit Trap that has gone off) in your place. One sprung trap is about one chance in twenty each time it looks.
- It can take: a Fox, Badger, Boar, Doe, Stag or Wolf Carcass, or a tied Boar. Small game is the eagle's, so a Deadfall can raise the odds and leave it nothing, and the visit ends with no message.
- It also reaches any placed store in the place, so a catch kept in a placed chest, shelf or basket can be taken too.

### Heron (`Heron`)
Eats a few small fish from a fishpond. Needs Water-Driven Infrastructure: it visits only that mod's filled or stocked Fishpond.

- Outside winter only, at most once every three days, and not in the first two weeks after the mod is added.
- Each visit lowers one pond's Minnow by 2 and its Perch by 1. It never touches the Pike or the pond itself. The Winter Fishpond is left out.
- No popup: you see the pond's fish bars drop.

### Raid pressure
`RaidPressure` in the config (`Low`, `Normal`, `High`) changes how often the eagle, black bear and heron come, by changing the odds of each roll. It does not change the wolverine, the world events, the animals or the bandit.

## Hazards

### Adder (`Adder`)
In summer, 09:00 to 18:00, outdoors in one of 24 open or rocky places, an **Adder!** card may appear. **Grit your teeth** gives a Bite Wound and about four hours of venom: +30 pain and slowly rising stress, never nausea.

- A roll is made at most once every two days, and not in the first five days after the mod is added. Bare-footed, a roll is an adder one time in five.
- Leather Shoes cut that to about one in eleven, Bark Sandals to about one in seven. Leggings make no difference.
- Caves, river and lake banks and wet clearings are left out. Which places count was judged from their names and has not been confirmed in play.

### Smithing burns (`SmithingBurns`)
Finishing one of the 30 smithing recipes (blanks for axe, pick, hammer, knife and spear heads, metal bar, hook, needle, wire, torc, ring, and the iron remaking steps) with Smithing under 40 and bare hands gives about a one in seven chance of a Fire Burn wound. Leather gloves, fur mittens or Smithing 40 prevent it. It rolls only for your own builds. Casting moulds and handle-fitting steps never roll. Not yet confirmed in a running game.

### Foraging bites (`ForageBites`)
Outside winter, foraging one of the 25 thicket places (alder, oak and pine thickets, tangles, hag and muddy ways, cliff and needle slopes, riverbank thickets) may leave you bitten by insects: about one forage in four with nothing against it. One bite means Pain and lost comfort for about six hours. Shoes (Leather Shoes, Bark Sandals), gloves and bug-proof cloth (a Large Cloth, a Hoodie) together cut the odds to about a tenth. Not yet confirmed in a running game.

### Mud volcano (`MudVolcano`)
With Forest Lore installed, its Mud Volcano may erupt while you stand outdoors in that place. A **The Mud Volcano Erupts** card appears; **Shield yourself** ends it. Rolled at most once every twelve hours, with about one roll in five bringing an eruption. An eruption always adds 30 Filth, and about two in five also burn you. Nothing else is touched.

## World events

Each raises a Moderate alert when it begins and shows an icon in your status strip while it lasts. Each has its own switch.

- **Drought** (`Drought`). Summer only, after at least four days without rain; about one such day in four. For about a week no rain falls, so rain catchers, cisterns, rain barrels and garden plots stop being refilled. Wells, ponds and rivers are unaffected. The Everrain perk prevents it.
- **Insect plague** (`InsectPlague`). Summer only; about one day in twelve. For about three days, swarms outdoors bite you far more often, adding pain and lost comfort. They do not follow you indoors. Rain, a camp fire, snakegrass in a hearth and staying indoors thin them. A Large Cloth or Hoodie gives a little protection (not yet checked in play).
- **Crop blight** (`CropBlight`). Outside winter, while a garden plot stands where you are; about one such day in twelve. For about two days, fungus spreads through the planted crops in garden plots in the place you stand in, and damages them once the infection is high. Gardens you do not visit are untouched. Fungicide or garlic water ("Apply Fungicide") and not overwatering slow it. It ends by itself.
- **Blood moon** (`BloodMoon`). At dusk on a full moon, about one full moon in three, and never in a new run's first week. The moon turns red and two extra lynxes prowl the lynx's four areas until dawn. A killed one stays dead until the moon ends. The extras need `Lynx` on too.
- **Eclipse** (`Eclipse`). At noon on a new-moon day in the first ten days of summer, about an even chance. For about an hour the light falls sharply, and for one quarter hour it falls to nothing (Very Dark), so tasks that need light are refused then. Whether the screen dims is unmeasured.
- **Aurora** (`Aurora`). On a dark winter night (new moon or waxing crescent), the first time you stand under the open sky; about one such night in three. For up to ten hours your mood rises a little every quarter hour while you are outside. It helps a low mood and does little above about 100.
- **Cold snap** (`ColdSnap`). Mid-spring and mid-autumn only, never at the season's edges; about one day in eight. For about two days the base temperature drops by 10, so nights, mornings and evenings reach the freezing state. It is not cold enough to freeze ponds. Whether it changes how cold you feel, or turns rain to snow, is unconfirmed.
- **Algae bloom** (`AlgaeBloom`). Summer only, after three dry days or during a drought; about one day in six. For about three days rivers and lakes turn green and only minnows bite: perch, eel, trout, char, pike and sturgeon cannot be caught. Water stays safe to drink, funnel traps are unaffected, and Water-Driven Infrastructure's fishponds are left alone. Not yet confirmed in play.

A switched-off event stops new ones. For most, one already under way runs its course; insect plague, crop blight, blood moon and aurora stop at once.

## Afflictions

Each is a status with an icon and an alert at each new stage. Each has a recipe in Survival > Medical or Magic > Rituals, researchable from the start, that uses only base-game cards.

### Fleas and lice (`FleasAndLice`)
- **How you get it:** sleeping or napping with an Improvised Bed, Improvised Shelter or Hideout in the room (about 1 sleep in 10), or a Bedroll or Log Bed (about 1 in 20); Filth of 250 or more (about 1 day in 8); keeping a Dog Friend, or with Sirus23's Mod Collection a tamed wolf or fox (about 1 day in 20 each). Each hit moves you up a stage.
- **Cost:** three stages (Flea Bites, Fleas, Infested) take 10, 25 and 40 off Comfort and make stress build faster by 0.25, 0.5 and 1.0 a quarter hour.
- **Cure:** stay clean (Filth 100 or less recedes a quarter stage every six hours); smoke or rub of bog myrtle or juniper, or bug repellent salve (a dose clears about a stage and a third); or the Herbal Wash, made from bog myrtle, Dry Juniper Needles and a kept Clay Bowl. **Wash with Herbs** takes a stage and a half and 100 Filth.
- With Herbs and Fungi, drag one of its herbs onto the Herbal Wash for **Wash with Herb Infusion**, which clears the fleas completely.

### Tooth decay (`ToothDecay`)
- **How you get it:** reaching "tired of eating sweet things" with honeycomb or sugar (about 1 time in 3); eating a lot of fruit quickly (about 1 in 6); plain time (about 1 day in 60).
- **Cost:** three stages (Sore Tooth, Toothache, Rotten Tooth) add 30, 60 and 90 to Pain and make stress build faster by 0.25, 0.5 and 0.75 a quarter hour.
- **Cure:** a Chewing Stick (one Willow Twigs) takes half a stage per **Chew**. With Herbs and Fungi, **Chew with Herbs** takes a whole stage. A Tooth Puller (metal tongs worked from one Heated Metal Bar of any metal, three uses) offers **Pull the Bad Tooth** at Rotten Tooth only: half an hour, clears the decay, adds 40 stress at once. Painkillers dull the pain but do not cure it.

### Spirit sickness (`SpiritSickness`)
- **How you get it:** when you fail to bind the Lynx, Goat or Blood Spirit and it is angered, its angry card waits on the board until you press Continue. While it waits, a roll once a game day (two in three) may leave you spirit-sick. The base game's own spirits are never a source.
- **Cost:** three stages (Uneasy, Spirit Sickness, Spirit-Ridden) take 10, 20 and 30 off Focus and make stress build faster by 0.25, 0.5 and 0.75 a quarter hour.
- **Cure:** it never fades by itself. The Cleansing Smudge (bog myrtle and Dry Juniper Needles, Magic > Rituals) is burned at a lit campfire, fire pit, fireplace or hearth: **Burn the Smudge** takes half an hour and removes one stage.
- Not yet confirmed in a running game. The smudge uses the Dry Bog Myrtle picture for now.

### Dancing plague (`DancingPlague`)
- **How you get it:** **Dance in the Ring** at a fairy ring, at night (an hour). When the dance ends, one time in two you cannot stop.
- **Cost:** two stages (Restless Feet, The Dancing Plague) drain stamina faster (about 1 then 2 a quarter hour) and use food energy faster (0.5 then 1). No stress.
- **How it ends:** by itself, a quarter hour per point (a taken dance is about four hours; two can reach eight). **Burn the Smudge to Break the Dance** at a lit fire breaks it at once.
- Switching it off hides Dance in the Ring. Not yet confirmed in a running game.

### Both at once
Infested fleas (+1.0 stress) and a Rotten Tooth (+0.75) together add more stress than the game removes near the top of the range, so stress keeps climbing toward Depression until one is treated. Either alone can do the same if something else already adds stress.

## Spirits

These use the base game's own spirit system. You meet a spirit, bind it once with a Summoning Ritual, and call it again from Magic > Calling Rituals. Each is a copy of a base-game spirit's gates, costs and roll. You call spirits on purpose, so they have no switch. Held spirits last about a day and then leave. Spirits are not yet confirmed in a running game.

- **Lynx Spirit.** A lynx carcass (killed or caught) holds one. **Release the Spirit** gives Beast Affinity; the Summoning Ritual on the same carcass tries to bind it instead, with the Fox spirit's gates. Held, it raises Climbing Aid and Ambush Aid and feeds the Fox Urge. Its first binding and first meeting each give one Insight. It uses the Earth spirit's picture for now.
- **Goat Spirit.** No carcass holds it. Its Summoning is listed in Magic > Calling Rituals once you have any Insight, and builds only while a goat you keep (the penned female) is on the board, with twelve fresh clover, meadow grass or heather stems and one spirit herb. It follows the Deer spirit's gates. Held, it raises Climbing Aid and Resilience and feeds the Deer Urge.
- **Blood Spirit.** Also no carcass. Its Summoning and Calling both need the base game's Blood Spilled to be high, which lasts about a week and a half after a fight. They follow the Wolf spirit's gates, with a large-prey carcass offered (not spent) and a spirit herb for the summoning. Held, it raises Strength and Dodge Bonus and feeds the Wolf Urge. It has no tracking and its essence is no weapon.
- **Dragon Spirit.** A lindwurm carcass holds it. It binds like the Lynx Spirit. Held, it raises Strength and Alertness Aid and feeds the Fox Urge. It is as rare as the lindwurm.
- **Ancestor Spirit.** It lives in a **Stone Cairn** you build, as the base game's West Stone holds its spirit. After a vigil of about a day at the cairn, **Keep Vigil** brings "The Ancestors". Then the cairn's **Blessing** gives the **Ancestor Blessing** for a day or so: Courage up by 3 to 18 as it fades, less Loneliness, and it recharges after about three days. Unlike the West Stone's blessing it gives no combat armour and does not wear down your mental structure. The met cairn also offers the **Ancestor Enchantment** (an enchantable item, four stones and a flint). It shows the cairn's picture for now.

## Rites

Done on purpose, with the base game's own magic. No switch.

- **Mistletoe.** In winter only, foraging one of the fifteen oak places (twelve oak groves, three oak clearings) may turn up Mistletoe, about one forage in twelve. It works in saves you already have. Research **Bless the Grove with Mistletoe** (Magic > Rituals): it spends one Mistletoe in place of a Spirit Acorn and makes the base game's blessed grove, with the same requirements. The acorn rite still works. Both use the Frostleaf picture for now. Not yet confirmed in a running game.
- **Athame.** A bone-bladed ritual knife (Magic > Implements; a Bone Knife, charcoal and twine). It also butchers like a knife. Each rite costs one of its 40 uses. Not yet confirmed in a running game.
  - **Let Blood** (an hour) gives you the Arm Laceration wound and Beast Affinity +100, fading over about two weeks. It is refused while an arm wound is open.
  - **Call Rain** (two hours, once every two days) thickens the clouds so the weather refreshes straight away. Rain is likely, never certain, and snow comes instead in the cold.
  - **Incense.** Beside a lit Herbs and Fungi Incense Bundle, or Community Mod Chest's lit Incense Burner, Heather Burner or Juniper Burner, each rite has an extra button that gives more (150 Beast Affinity, or 300 Clouds) and burns up one point of that incense's fuel. Without those mods, only the base buttons appear.
- **Casting bones.** A handful of marked bones (Magic > Implements; three Bones and a Charcoal), good for 30 castings. **Cast the Bones** (half an hour) drops one card saying whether the eagle, the black bear, both or neither can come for your food within a day. The reading is always true, because it reads the same clocks and switches the raids wait on. A raid it names may still not come. The bones say nothing of the wolverine, heron, adder or world events. Not yet confirmed in a running game.

## Brews and witchfire

Made from base-game ingredients. No switch. Not yet confirmed in a running game.

- **Beast Brew** (bonemeal and fat), **Earth Brew** (ash and dry moss) and **Musclebrew** (a wild egg and dried pine nuts). Research each in Magic > Rituals and make it from a bowl of ordinary clean water; you keep the bowl.
- Drinking one quenches some thirst and starts a timer of about half a day (a second draught makes it a day). Beast Brew and Earth Brew raise Beast or Earth Affinity by 0.5 a tick (about 24 over half a day), the pace of the base game's Sacred Oak; each takes 8 hours to research. Musclebrew makes hard work and heavy loads grow muscle sooner, and does nothing if you only sit. Each shows in the status strip.
- **Witchfire** (Survival > Fire; witchstone dust and beeswax). Kindle it (a quarter hour) and it gives light for 12 hours while you carry it, then burns out to ash, which **Scatter the Ash** removes. It needs no fuel and gives no heat.
- All pictures are base-game stand-ins for now.

## Ritual objects

Made on purpose for the spirit path. No switch. Not yet confirmed in a running game.

- **Spirit Jar** (Magic > Implements; a fired Clay Jar, a spirit herb and charcoal). Takes a spirit's essence (up to 150), which the base game's clay jars refuse, and keeps it. Pour an essence in from a bound spirit while your mind is open. Later pour it into an Amulet or one of the coats below.
- **Spirit Mask** (Magic > Implements). Worn on the face. While your mind is open (altered mind 50 or more), Beast Affinity rises by 0.6 a tick. It wears out after about four weeks of wear.
- **Lynx, Goat and Blood Charms** (Magic > Implements). Each recipe appears once you have bound that spirit. Worn at the neck, each holds half of its spirit's gift (Climbing Aid, Resilience, Strength) at the lowest level, with no binding needed. They wear out after about four weeks.
- **Lynx, Musk Ox and Wisent Coats** (Tailoring > Leather, Leatherworking 15). Sewn from each beast's own tanned leather (medium, large and very large), two twine and a kept needle. Each gives warmth (the musk ox coat +7.5, as the base game's Bear Coat), comfort, armour (Torso 10 in a fight, as the base game's coats) and weather protection, takes a spirit essence, and with an open mind lends its beast's help: the lynx coat Climbing Aid and Ambush Aid (8.4 each, the size of the base game's Wolf Coat's two aids) and the Fox Urge, the musk ox coat Resilience, the wisent coat Strength. Whether each hide tans to the leather its coat needs is read from game data, not yet seen in play.
- **Beast Totem** (Magic > Shrines). A carved post for camp. While you are there, Beast Affinity rises by 0.5 a tick.
- **Stone Cairn** (Magic > Shrines; three heavy stones, then twelve stones). One to a place. Home of the Ancestor Spirit and a door to the Underworld.

## Fair folk

Rare night threats, a stone that keeps them kinder, and the fairy king and queen. Not yet confirmed in a running game, except where noted.

- **Banshee** (`Banshee`). At night, while you bleed badly, she may keen: a card tells you to bind the wound, and stress rises for some hours. At most once every three days.
- **Nixie** (`Nixie`). At night on a river or lake bank (27 places), a nixie may sing. Hold fast (Courage helps) or follow. Drawn in, you stay soaked, cold and uneasy for about four hours. At most once every four days.
- **Selkie** (`Selkie`, needs Far Territory). At night on Far Territory's Driftwood Shore, a selkie may sing from the surf, with the same choice and cost. At most once every three days. Without Far Territory she never comes.
- **Wild Hunt** (`WildHunt`). On a winter night the Hunt may ride for about five hours. Outside, each quarter hour adds stress and may bruise you. Under a roof nothing happens. At most once every six days.
- **Threshold Stone** (Magic > Shrines; a heavy stone and three small stones; one to a camp). Drag salt, a flatbread, a honeycomb or cheese curds onto it to leave an offering. For about three days the four threats above are less likely, never impossible. A Fairy Stone (**Offer a Fairy Stone**) keeps the offering for about six days; the threats come back gradually over its last three.
- **Fairy King and Fairy Queen.** They hold court in the Faerie Realm on every visit and never follow you home. **Speak with the King** or **Speak with the Queen** offers one bargain each, once a run, and only while you can pay. The King gives two Fairy Stones for 60 Willpower. The Queen takes away 40 Stress and gives a sprig of Mistletoe for 16 Stamina. Turn one down and it is offered again next time. Gifts land on the realm's board, so pick them up before you leave. Checked in a test harness, not yet in real play.

## Fairy ring and realms

### Fairy ring
Found, not made. Foraging one of seventeen oak and birch places (twelve oak groves, three oak clearings, Flower Glade, Heather Glade) may turn up a **Fairy Ring**, about one forage in thirty. It works in saves you already have. Only one ring stands on a board.

- **Pick Toadstools** (an hour): two or three fly agaric, and one time in twelve a Fairy Stone. Offered once every two days across all rings.
- **Dance in the Ring** (an hour, night only): see Dancing plague.
- **Enter the Ring** (no time, night under a full moon only): opens the Faerie Realm.

### How the realms work
There are three: the Faerie Realm, the Moon Realm and the Underworld. You go on purpose, so the realms have no switch. Each door opens its own copy of the realm, and what you leave there waits for your next visit through the same door.

- You always come home to the grove, shrine or cairn you used. An exit card has a button home at once and counts the stay down, then sends you home whatever you are doing. A save and reload inside keeps the count.
- While you are away, no raid rolls for your food. The one that was due rolls when you are home.
- The lynx, blood moon lynxes, bandit, lake beast, frost demon and lindwurm cannot attack you there, nor can Sirus23's Wild Owl. You can still approach them. The base game's own wildlife can follow you in, as into a cellar.
- Each realm has finds on its board (pick them up before the time runs out) and one danger, each with its own switch. Dangers never keep you from going home.

| Realm | Door | Stay | Finds | Danger |
|---|---|---|---|---|
| Faerie Realm | Fairy Ring: **Enter the Ring**, night under a full moon | 4 hours | **Wander the Moonlit Meadow** (2 h): Fairyweed; by chance Spirit Mushrooms, Mistletoe, a Fairy Stone | `FaerieTricks`: about hourly, one in five, **Pixie-Led**; **Find Your Way Back** costs 10 Stamina |
| Moon Realm | **Moon Basin** (Magic > Shrines; a clay bowl and two small stones; only beside a Moon Stone, one to a place): **Step into the Moonlight**, night under a full moon | 3 hours | **Search the Silver Shallows** (2 h): feathers; by chance moon moss, Niter Crystals, Flint | `MoonGlamour`: about hourly, one in five, **Moonstruck**; **Shake Off the Glamour** adds 20 Stress |
| Underworld | Stone Cairn: **Descend Beneath the Cairn**, at night. With Forest Lore, its Ancient Cairn offers **Go Down the Tomb Stair** | 3 hours | **Search the Barrow Halls** (2 h): Bones; by chance Ash, Bird Bones, a Sulphurous Stone | `RestlessDead`: about hourly, one in five, **The Restless Dead**; **Break Free** costs 14 Stamina and adds 12 Stress |

The realms were checked in a test harness (going in, the clock running out, save and reload, raids held back, finds, dangers), not yet in real play, and the Forest Lore door not yet with Forest Lore installed. Their pictures are mostly base-game stand-ins.

## Wandering shaman

A visiting trader, with no switch. He comes to the Green Glade on the eighth day of every season and stays one day, a week before the base game's own trader comes there in spring and autumn. Before each visit he restocks with dried spirit mushrooms, dried fairyweed, dried fly agaric, the three spirit charms and winter mistletoe, sold at half again their worth.

Once you have bound the Lynx, Goat or Blood Spirit, talk to him and he teaches one calling for a spirit you have not bound yet, once a run. Not yet confirmed in a running game.

## Config

File: `BepInEx/config/crispywhips.harsh_wilds.cfg`. A change takes effect after a full restart of the game, not a save reload. Every switch below is in `[Threats]` and defaults to `true`; `false` turns that threat off.

| Switch | What it turns off |
|---|---|
| `Lynx` | The lynx. Turning it back on returns it about a week later, not at once. |
| `Wisent`, `MuskOx` | The wisent, or all three musk oxen. |
| `Bandits` | The bandit, and with him the theft. |
| `Eagle`, `BlackBear`, `Wolverine`, `Heron` | That raid. The heron and mud volcano also need their partner mod. |
| `Adder`, `SmithingBurns`, `ForageBites`, `MudVolcano` | That hazard. Venom already running ends on its own. |
| `Drought`, `InsectPlague`, `CropBlight`, `BloodMoon`, `Eclipse`, `Aurora`, `ColdSnap`, `AlgaeBloom` | That world event. `BloodMoon`'s extra lynxes also need `Lynx`. |
| `FleasAndLice`, `ToothDecay`, `SpiritSickness`, `DancingPlague` | New cases, and clears any you have. `DancingPlague` also hides Dance in the Ring. |
| `Banshee`, `Nixie`, `Selkie`, `WildHunt` | That fair-folk threat. |
| `WhiteWisent`, `LakeBeast`, `FrostDemon`, `Lindwurm` | That rare beast. One already in the world leaves after a full restart. `WhiteWisent` also needs `Wisent`. |
| `FaerieTricks`, `MoonGlamour`, `RestlessDead` | The danger in that realm. The realm itself has no switch. |

Section `[Tuning]`: `RaidPressure` is `Low`, `Normal` (default) or `High`; see Raid pressure.

**A gentler game.** Every threat is on by default, because that is what the mod is for. For the forest's powers and creatures without its harshest surprises, set these to `false`: the rare beasts (`WhiteWisent`, `LakeBeast`, `FrostDemon`, `Lindwurm`), the fair folk (`Banshee`, `Nixie`, `Selkie`, `WildHunt`) and the afflictions (`FleasAndLice`, `ToothDecay`, `SpiritSickness`, `DancingPlague`). Then restart the game.

## Compatibility

- Works alone. No other content mod is needed.
- **Herbs and Fungi:** its herbs make a stronger dose in the Herbal Wash and Chewing Stick, and its Incense Bundle strengthens the athame rites.
- **Community Mod Chest:** its lit incense burners strengthen the athame rites.
- **Water-Driven Infrastructure:** the heron can visit its fishponds. Without it the heron never comes.
- **Sirus23's Mod Collection:** the eagle also takes owls; a tamed wolf or fox counts as a flea carrier and keeps the eagle away; a tamed wolf keeps the black bear away. Its Wild Owl cannot attack you in a realm.
- **Advanced Copper Tools:** the black bear also takes Salt-Cured Meat.
- **Forest Lore:** its mud volcano can erupt, and its Ancient Cairn offers a way down to the Underworld.
- **Far Territory:** the selkie comes to its Driftwood Shore.

Without a partner mod, its entries simply do nothing, and the framework's load log may list them as unresolved.

## For other mods

These UniqueIDs are a contract: Harsh Wilds will not rename or remove one without saying so in its changelog, so another mod may read them, check them or name them in a guard.

- Raid event cards: `hw_event_eagle`, `hw_event_blackbear`, `hw_event_wolverine`. A raid is resolved the moment its card appears and cannot be waited out: the card stays on screen until its button is pressed, and a raid rolled during sleep or travel shows when that action ends.
- Encounters: `hw_encounter_lynx`, `hw_encounter_wisent`, `hw_encounter_whitewisent`, `hw_encounter_lakebeast`, `hw_encounter_frostdemon`, `hw_encounter_lindwurm`, `hw_encounter_muskox`, `hw_encounter_bandit`.
- Species and the UniqueID of each species' agent (the framework derives it as the MD5 of `csffmfw:animal:<SpeciesId>:agent`, written as 32 lower-case hex characters):
  - `hw_lynx`: `0011de544098ec6a13d58401b1e57ed6`
  - `hw_lynx_bm1`: `99739fbaaab1115935d6c91a6a55161d`
  - `hw_lynx_bm2`: `8850df8a9713b9a1d841de23454f6750`
  - `hw_wisent`: `a108a4c8e1e3320c4eb698fb7726d651`
  - `hw_muskox`: `c1aa9b519e359622f39643b429dee637`
  - `hw_muskox2`: `0d0136e2982b48520c666fecac91c578`
  - `hw_muskox3`: `f571b9399b6e091ebabc39f412841d61`
  - `hw_bandit`: `dceca78df703345480f33de23207e32c`
  - `hw_whitewisent`: `71e0aadabeab63439637465e07781dcd`
  - `hw_lakebeast`: `d9a558a7d39e26cb7f468b59b95caf46`
  - `hw_frostdemon`: `c443b4053a7413294b5b01e252b80044`
  - `hw_lindwurm`: `bc8c1c79f001823ce106881987ee028e`
- Switch stats (1 while that threat is switched on, 0 when off; a reader may check them): `hw_switch_lynx`, `hw_switch_wisent`, `hw_switch_muskox`, `hw_switch_eagle`, `hw_switch_blackbear`, `hw_switch_wolverine`, `hw_switch_bandits`, `hw_switch_drought`, `hw_switch_insectplague`, `hw_switch_cropblight`, `hw_switch_bloodmoon`, `hw_switch_eclipse`, `hw_switch_aurora`, `hw_switch_fleasandlice`, `hw_switch_toothdecay`, `hw_switch_heron`, `hw_switch_adder`, `hw_switch_coldsnap`, `hw_switch_algaebloom`, `hw_switch_smithingburns`, `hw_switch_foragebites`, `hw_switch_mudvolcano`, `hw_switch_whitewisent`, `hw_switch_lakebeast`, `hw_switch_frostdemon`, `hw_switch_lindwurm`, `hw_switch_spiritsickness`, `hw_switch_dancingplague`.
- Spirit stats (each 0 to 1, set to 1 the first time and never reset, as the base game's are): `hw_stat_lynx_spirit_bound`, `hw_stat_lynx_spirit_encountered`, `hw_stat_goat_spirit_bound`, `hw_stat_goat_spirit_encountered`, `hw_stat_blood_spirit_bound`, `hw_stat_blood_spirit_encountered`, `hw_stat_dragon_spirit_bound`, `hw_stat_dragon_spirit_encountered`. The bound spirit cards are `hw_spirit_lynx`, `hw_spirit_goat`, `hw_spirit_blood` and `hw_spirit_dragon`. The Ancestor Spirit's, in the West Stone's shape: `hw_stat_ancestor_magic` (0 to 3, hidden; 2 once the Ancestors have answered), `hw_stat_ancestor_propinquity` (hidden, rises at the cairn) and `hw_stat_ancestor_blessing` (0 to 288, the blessing's effect stat); the cairn is `hw_cairn`.
- Realms: the three realm environments `hwenvfaerierealm`, `hwenvmoonrealm` and `hwenvunderworld`, their location cards `hwlocfaerierealm`, `hwlocmoonrealm` and `hwlocunderworld`, and the exits `hw_faerie_exit`, `hw_moon_exit` and `hw_underworld_exit`. Presence flags (hidden, 1 while the player is in that realm, 0 elsewhere): `hw_stat_in_realm` for any realm, and `hw_stat_in_faerie`, `hw_stat_in_moon` and `hw_stat_in_underworld` for one. Danger switch stats: `hw_switch_faerietricks`, `hw_switch_moonglamour` and `hw_switch_restlessdead`. A realm is an instanced environment and never a map node.
- A guard stops a Harsh Wilds fight only if it names that encounter or animal: through the encounter's UniqueID, or through the animal's agent UniqueID in its agent list. A guard that names neither does not stop the lynx or the bandit.

## Version history

Full notes in `CHANGELOG.md`. Latest:

- **0.11.7:** the Tooth Puller is metal tongs made from a Heated Metal Bar, and the selkie can now rise on Far Territory's shore.
- **0.11.6:** balance pass: musk oxen give less and return monthly, slower brews and athame rites, base-game coat tiers with real armour, a fuller lindwurm, a use for Fairy Stones.
- **0.11.5:** the golden hind is removed; the base game's White Deer covers it.
- **0.11.4:** the Faerie Realm stays still; the people, animals and spirits still breathe.
- **0.11.3:** the shaman, the bandits, the frost demon, the animals and the spirits breathe, on their cards and in their encounters.
- **0.11.2:** fixes for spirit sickness rolling forever, the crop blight stalling, and the Fallen Bandit's weight.
- **0.11.1:** the dancing plague roll is one in two as promised, and Witchfire ash can be scattered.
- **0.11.0:** the Faerie, Moon and Underworld realms, the fairy king and queen, the athame and casting bones, the wolverine, the wandering shaman, and more.

## Licence

MIT. See `LICENSE`.
