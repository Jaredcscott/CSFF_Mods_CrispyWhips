# Herbs and Fungi: Changelog

All notable changes to this mod. Dates are release dates.

---


## [1.14.15] - 2026-10-22

Needs CSFF Mod Framework 2.28.9 or later.

### Changed

- **Ful and the Herbal Salve take any oil.** Ful no longer needs Peanut Oil and the salve no longer needs Hemp Seed Oil: any bowl of oil works, from this mod, Advanced Copper Tools or another mod. The salve shows up in the Medical tab as soon as you hold any oil. With Sirus23's Mod Collection the salve still takes Lanolin.
- **Herbal oils are no longer food.** The seven pressed herbal oils (Appleweed, Chamomile, Fairyweed, Frostleaf, Ginseng, Hemp and Yarrow) were only ever applied, never eaten, but were tagged as food, so a partner could try to eat one, raiders could take one as food and food shelves accepted them. They are now oils only: you still apply them, and they still fuel lamps.
- **Hemp Seed Oil's description** no longer says to render it into lamp oil (Advanced Copper Tools retired that recipe, since every oil already fuels a lamp). The README's Advanced Copper Tools note no longer describes that recipe either: it now says every pressed oil fuels ACT's lamps as it is.
- **Turnroot Flatbread is a dense little brick of turnroot starch.** It now nourishes like Dried Turnroot, a little less filling, instead of borrowing the rye flatbread's values, and it has its own picture. Thanks to Banana Monk for the suggestion.
- **Turnroot Chips can be eaten as they are**, with the same nourishment as Dried Turnroot, and they have their own picture instead of the whole turnroot's.

### For mod authors

- The salve's oil slot is `GpTag_HnF_SalveBase` from the new `TagGroups.json` (every non-liquid `tag_Oil` card plus `sh_lanolin`), replacing the `BlueprintAlternates.AddAlternateIngredientIn` call in `Patcher/GameLoadPatch.cs`; its visibility is `TagsOnBoard` `tag_Oil` (was `CardsOnBoard` Hemp Seed Oil). Ful's slot carries the framework's `GpTag_FW_OilIngredient`. Both keep their oil as `RequiredCard`.
- `HerbalOil_*.json`: `tag_HumanFood` and `tag_Plateable` removed from `CardTagsWarpData`.
- `HempFlower.json`: "Harvest Seeds" was a `CardInteractions` entry with all four triggers empty, so it never rendered. It is now a `DismantleActions` entry (`HandAction`, `DaytimeCost` 1, `ModType` 3, 1 to 3 `herbs_fungi_hemp_seeds`), and its keys are `Herbs_And_Fungi_HempFlower_DismantleActions_HarvestSeeds_ActionName` and `_ActionDescription`.

### Fixed

- **Behind the scenes:** with Community Mod Chest installed, a missing Herbalism graduate perk is now a default-visible log warning (it was a debug-only line), so a missing Identify (Academy training) action can be diagnosed.
- **Hemp Flower's Harvest Seeds button shows up.** The card said it could be harvested for seeds, but the action never appeared. It is now a button on the flower: Harvest Seeds uses up the flower and gives 1 to 3 Hemp Seeds.
- **Elderflower Cordial no longer claims to cool you.** Its description said it was cooling on a warm day, but it never lowered your temperature (the iced teas and Shaved Ice do). It now says what it does: soothing, and a little thirst-quenching.
- **The Oil Press help is accurate.** It said every herbal oil matches smoking that herb in a pipe, but chamomile, ginseng and yarrow have no pipe, and their oils' descriptions said the same. The help now names the four oils that match a pipe (hemp, fairyweed, appleweed and frostleaf), lists every pressing (flax, peanuts, pine nuts and the chamomile, ginseng and yarrow oils were missing) and mentions the Linseed Cake. The Chinese help, which only covered hemp seeds and truffle, now says the same. Four herbal oils also promised more than they give: Yarrow oil said it heals, Frostleaf oil that it eases pain, Hemp oil that it relieves stress and Ginseng oil that it energizes, but every herbal oil only lifts your mood. Their descriptions, help and Apply text now say so, in English and Chinese.

## [1.14.14] - 2026-10-22

### Fixed

- **The mod list description names every grow bag that keeps growing through winter beside a lit fire:** Kale and the six herb grow bags (Chamomile, Dandelion, Plantain, Yarrow, Rhubarb and Healer's Moss), not just Kale.

## [1.14.13] - 2026-10-22

### Fixed

- **Peanut Butter has its own picture.** It was showing the Roasted Peanuts art; it now shows a clay bowl of creamy peanut butter, like the other bowl items. Thanks to Banana Monk for the report.
- **The pickling recipes no longer say they need a closed Pickle Vat.** All eight (plain and spiced vegetables, mushrooms, meat and frogs) are done in the open Pickle Vat and close it, so they now read "Pickle Vat Needed".
- **Forager's Seasoning names its powders by their real names.** Its recipe and help text asked for "Ground" Black Trumpet, Shiitake and King Oyster; they now say Black Trumpet Powder, Shiitake Powder and King Oyster Powder, as the cards are called. The Chinese text named a different mushroom for the King Oyster Powder and now matches the cards too.
- **The six Mushroom Log recipes lost a stray comma** at the end of their description.
- **Seven recipes name the exact card that unlocks them.** The four Oil Press parts, the Press Bed Frame and the Wooden Pantry now say "Unlocked after obtaining a Wooden Plank", the Haggis says "a Cleaned Stomach" and the Gruit Bundle says "Dry Bog Myrtle Leaves", as those cards are called. The Chinese Gruit Bundle text now uses the card's Chinese name too.
- **Clearer log warnings if a game update breaks one of the mod's patches.** The Apothecary tea unlocks, Dig for Truffles, the Pouch's powder storage, the brine colour change on a Pickle Vat and the pickle recipe slot names now write a warning to the log instead of failing silently. Dig for Truffles is now left out, rather than added without its truffle, if its drop cannot be built. Nothing changes in normal play.


## [1.14.12] - 2026-10-19

Needs CSFF Mod Framework 2.28.6 or later.

### Added

- **Herb Grow Bags: grow herbs through winter beside a fire.** Chamomile, Dandelion, Plantain, Yarrow, Rhubarb and Healer's Moss each get a **Pack ... Grow Bag** blueprint (Farming > Agriculture, unlocked once you have that herb) that works like the Kale Grow Bag: 1 Wood Shavings and 2 of the herb, no fertile ground needed, a daily **Tend**, and in winter it only keeps growing and stays alive while a lit fire is on the same board. Without a fire it dies into a Dead Crop in about two winter days. After 15 days it turns into the herb's ordinary patch, so you harvest it the usual way for 2 to 3 of the herb. The field crops (Oats, Hemp, Grapes, Broad Beans, Hops, Peanuts, Leeks, Carrots, Ginseng) still grow outdoors only.

### Changed

- **Rinsing Peanut Pods works both ways and with water in any container.** Drag a bowl, pot or waterskin of water onto the pods, or drag the pods onto the water. Each rinse now uses 100 water (it used none before), so the container needs at least that much. Water, River Water and Unsafe Water all work; milk does not.

- **Pickled vegetables, mushrooms and frogs now keep for about a month, not three.** The ready vats, plain and spiced, now spoil as fast as pickled meat has since 1.14.9. A ready vat already on your board keeps the time it has stood, so one that has stood longer than about a month spoils soon after you update.

### Fixed

- **The Pack Kale Grow Bag blueprint said it takes soil.** It takes 1 Wood Shavings and 2 Kale, and now says so. The Chinese help text on the Kale Grow Bag also read wrongly where it says to let the fire go out.


## [1.14.11] - 2026-10-17

Needs CSFF Mod Framework 2.28.6 or later.

### Fixed

- **The lit Incense Bundle no longer swells to fill the card.** Its moving picture was still on the old cropped canvas, so the card stretched it over its whole face and under the name banner whenever they played (the same problem the still pictures had, reported by Chiwei). They now sit exactly where the still picture does.
- **Wood and candles no longer lose all their fuel when you wash at a river.** With this mod installed, "Wash yourself" on a River set the fuel of every card on the board to 0: Oak Wood did nothing on a fire and candles burned out and vanished. The fix is in CSFF Mod Framework 2.28.6, so update the framework; nothing in this mod changes. Items a wash already drained stay drained. Thanks to Banana Monk for the report and the save.
- **Cook bars show their icon.** Chicken of the Woods, Dried Chicken of the Woods, Dried Chanterelle and Puffball showed a blank white circle on their Cook bar; it now shows the cooking icon like every other Herbs and Fungi Cook bar.


## [1.14.10] - 2026-10-16

Needs CSFF Mod Framework 2.28.5 or later.

### Added

- **Kale Grow Bag: fresh food in winter, at the cost of a lit fire.** A new **Pack Kale Grow Bag** blueprint (Farming > Agriculture, unlocked once you have Kale) packs 1 Wood Shavings and 2 Kale into a grow bag. It needs no fertile ground, so you can set it down indoors. The bag grows kale like an ordinary patch and takes the same daily **Tend**, but in winter it only keeps growing and stays alive while a lit fire is on the same board; let the fire go out in winter and it dies into a Dead Crop in about two days. This is the paid winter-growing option that replaces the free winter crops the balance pass removed. The warming rates are placeholders, still tunable. The fire is what keeps it alive, indoors or out. The bag is simply the only kale that can sit indoors, and it does not need to be indoors.

### Changed

- **Eating Cut Ginger or Dried Ginger now gives the base game's Ginger status, which nothing in the base game gave.** Small Dose: Pain -25, Courage +50, Appetite +50, Immune System +50, a little more Wakefulness, less Nausea and looser stool, fading at 1 point per 15 minutes. Cut Ginger adds 12 and Dried Ginger 16 (about 3 and 4 hours). These doses are placeholders and still tunable, not balanced. Their old Eat stats are unchanged.
- **Burning Dried Hemp Flowers or Hemp Powder on a fire now lifts Morale by 10 to 12**, the same as smoking hemp in the Herb Pipe (it was 15 to 20 and 20 to 25, above every pipe). The Altered Mindstate cost of burning is unchanged.

### Fixed

- **Raw Pea, Nettle and Sprouted Rye Bread no longer bake into spoiled bread.** Since 1.14.9 a baked loaf keeps for less time than its dough, so dough that had sat a while came out of the fire already spoiled. The bread now takes three quarters of the dough's spoilage.



## [1.14.9] - 2026-10-15

Needs CSFF Mod Framework 2.28.5 or later.

### Removed

- **The Foraging Forest is gone.** Its seven places west of Primeval Woods (Foraging Path, Pine Clearing, Highland Meadow, Oak Clearing, Misty Falls, Alder Woods and the Old-Growth Grove) are no longer on the map. Far Territory now holds the mod-made locations, and these plants belong in the vanilla woods. Each of those places was a copy of a vanilla one, so every plant they had is now foraged at the vanilla original, in the same seasons. The plants that grew only in the forest keep their forest chances; a herb the vanilla place already gave (chamomile, dandelion, plantain, currants, chaga and the like) keeps the chance it already had there, so nothing you already forage changes:

| Was found at | Now foraged at |
|---|---|
| Foraging Path | Secret Grove |
| Pine Clearing | Pine Clearing (vanilla) |
| Highland Meadow | Pine Meadows |
| Oak Clearing | Mossy Clearing |
| Alder Woods | Alder Grove |
| Old-Growth Grove | Old Grove, which needs no unlocking |
| Misty Falls | Greenfalls (it had no forage of its own) |

- All 24 plants that grew only in the forest move with it: Camas Bulb, Chestnuts, Chicory Root, Crabapples, Elderberries, Elderflowers, Fiddleheads, Goosefoot Greens, Hazelnuts, Horseradish Root, Mandrake Root, Mustard Seeds, Wild Pears, Wild Plums, Sloes, Thistle Greens, Kale, Leek, Carrot, Broad Beans, Hops, Grapes, Oats and Rhubarb. Truffles, Morels and Cloudberries from the Old-Growth Grove are now found at the Old Grove.
- **Gone entirely:** the Wild Bees' Nest (Honeycomb itself is vanilla and still used by every honey recipe), the three forage hotspots (Lingonberry, Cloudberry and Chamomile Patches), the Fungal Bloom, the Overgrown Forest Trail and the Old-Growth Grove unlock.
- Card help texts, the Botanical Field Guide and the README name the vanilla places now.

### Saves

- A save made inside the forest loads at the vanilla place that forest place copied, with the cards that were on its board. This uses the framework's UID aliases (`UidAliases.json`), which is why 2.28.5 is needed.

### Changed: vanilla-balance pass

- **Planted crops need looking after.** Every planted herb, vegetable, vine and field grows three times as long (15 to 30 days), gives a little less (Kale, herbs, Carrot, Ginseng and Mossbed 2 to 3; Grapes 3 to 5; Hops and Peanuts 2 to 4; Oat and Broad Bean fields 6 to 9), and has Health and Care like the base game's crops. **Tend** it once a day (1 hour, a little stamina). Left untended it dies in about ten days, and in winter it stops growing and dies in about two days, leaving a Dead Crop to clear away. Crops planted in an older save start healthy.
- **Hemp no longer grows mushrooms.** The hemp plant and field gave Golden Oysters, King Oysters and Morels; they now give only stalks, flowers and seeds.
- **Mushroom logs are slower and smaller.** A log colonizes in about 15 days, stops in winter and gives 8 mushrooms; a spore print comes about 3 harvests in 10. Grow bags give 6.
- **Mood boosts are on the base game's scale.** Hemp Butter gives Morale 10, 20 or 30 by dose (was 90, 180, 270), herbal oils match their pipes, and Cooked Truffle, Truffle Butter and Chanterelle Broth lift Morale less. The Fairyweed pipe stays the strongest pipe.
- **Calming teas and remedies are gentler**: Reishi Tea lowers Stress by 15, Ginseng and the Vigor Elixir by 12, the other teas by 12, dried calming herbs by 8.
- **Broths quench thirst instead of filling your water.** The four mushroom broths are simmered in a clay bowl of freshly boiled water (at least 150), quench thirst and feed you a little (30 calories). Teas, Tendon Soup and Shaved Ice quench thirst too.
- **Nuts, berries and mushrooms keep and weigh like the base game's.** Nuts and roasted nuts weigh more and fill less, roasted nuts keep about 8 days; fresh berries keep 3 days, dried ones about 19; raw mushrooms keep 3 days, cooked ones 1.5, dried ones about 19.
- **Breads keep about 4 days, Herb Cheese about three weeks, pickled meat about a month** (and fills a little less).
- **Forage finds are half as likely.** Every plant and mushroom this mod adds to a Forage action turns up half as often as before, including those moved from the Foraging Forest (Velvet Shank 7%, Watercress 5%, Cloudberries 6% at the Old Grove). Truffle digging is unchanged.
- **Preserves have 2 servings**, each half of the old jar.
- **Yarrow stops bleeding half as fast**, and Healer's Moss Tincture's pain relief lasts about a day.
- **The Death Cap Ward turns away 25% of wild-animal attacks** (was 45%), and its death caps rot in about ten days, placed or carried.
- **The Apothecary Shelf keeps herbs like a pantry** (75% spoilage) and is safe from raiding animals; the shelf and the Wooden Pantry take a day to research.
- **Perk prices**: Herbalist's Advantage costs 15 Suns, Add Hemp 5 Suns.

## [1.14.8] - 2026-10-14

### Fixed

- **Card pictures sit where they belong again.** In the previous update every item picture was stretched to fill its whole card, sitting high with the name banner over it. The pictures are back in the game's own framing: centred, low on the card, with space above, like the vanilla cards beside them. Thanks to CHIWEI for the report.

## [1.14.7] - 2026-10-14

### Changed

- **Meat dries more slowly on the Drying Tray.** It dried at the vanilla Drying Rack's speed with none of the rack's smoke upkeep, so Bird Meat was dried in 6 hours. Meat, fish, organs and sausages now dry at a quarter of that speed (about a day for Bird Meat). Herbs, mushrooms and berries dry as before. Thanks to Banana Monk for the report.

### Fixed

- **Spoilage shows the bacteria icon** on 98 cards that showed the hourglass, as vanilla food does.
- **Drying shows the hammer icon** on 32 mushrooms, herbs and berries that showed a red icon that looked like the immune system's.
- **Berries, mushrooms and herbs taste of something when eaten straight.** 106 Eat buttons never counted the food's flavours, so they only tasted of anything once eaten from a bowl. Vanilla food counts them either way, and so does this mod's now.

## [1.14.6] - 2026-10-14

### Changed

- **The forest trails stay still again.** Their slow drift (1.14.5) is off for now. The berry and chamomile plants still sway.

### Fixed

- **Hemp Butter no longer spoils the moment it solidifies.** Active Hemp Butter keeps for 18 days but the finished butter for 5, and the old spoilage was carried over as-is, so anything past about a quarter spoiled landed on a full bar and rotted. It now carries the same share: a half-spoiled Active Hemp Butter gives a half-spoiled Hemp Butter. Thanks to Banana Monk for the report.
- **The same fix for raw pasties, Inactive Hemp Butter and Sliced Horseradish.** A mushroom or berry picked a day or more earlier made a raw pasty that rotted at once, because the pasty only keeps for one day; Hemp Flower Powder past half spoiled and Horseradish Root past 40% did the same. Each now keeps the share of freshness its ingredient had.

## [1.14.5] - 2026-10-14

- **The forest trails drift and the berry and chamomile plants sway a little.** Needs CSFF Mod Framework 2.28.3.
- **The Smouldering Incense Bundle is animated:** its smoke curl drifts and the burning tip glows. The unlit bundle stays still.
- **The Wild Bees' Nest is animated:** the bees at the entrance buzz about.

## [1.14.4] - 2026-10-13

- **Honey foods are worth their honeycomb again:** game EA 0.69c made Honeycomb a 168-calorie food, so honeying or preserving lost calories. Honeyed Berries (133), Honeyed Nuts (182), Berry Preserve (433), Plum Preserve (387) and Rhubarb Preserve (345) now give back at least what goes into them.
- **Honey help names every honeycomb source, and Wax by its new name:** since game EA 0.69c vanilla beehives and bee skeps give Honeycomb too, so the help text lists them beside the Wild Bees' Nest; the Wax Leather Dressing help and the Linseed Oil button say Wax, the game's new name for Beeswax.
- **The Wild Bees' Nest shows its honeycomb icon again on game EA 0.69c:** the update renamed the vanilla honeycomb picture, so the stat icon fell back to the default glyph.
- **Starter-kit perks hand their items over as ordinary starting items.** Thirteen perks (Add Fungi, Add Hemp, Alchemist, Apothecary, Culinary Kit, Edibles Kit, Fungal Cultivator, Master Herbalist, Medical Mushrooms, Oil Press, Seed Bag, Smoke Kit, Hemp Farmer) listed their items as worn equipment, but none can be equipped, so they always landed on the ground. You get the same items at the same quality; the perk text now says so.
- **Forager's Notebook keeps its entries.** If a game update ever changes how the notebook's saved text is read, it stops updating and keeps what it lists, instead of rewriting itself with only the newest find.
- **ForageDropDensityScale above 1.0 stops at a certain drop.** Values over 1.0 still make finds more common, but no drop chance (or seasonal boost) goes past 100%. 1.0 or lower behaves as before.
- **Teas, tinctures and remedies have realistic weights.** They weighed 0.1 to 0.3 before. Anti-Nausea Tea and Sleep Tea now weigh 400, Death Cap Tincture, Healer's Moss Tincture and Herbal Tincture 350, and Pain Relief Powder 320 (a Clay Bowl is 300, plus contents). Chamomile Tea, Dandelion Tea, Elderflower Cordial, both iced teas and Stimulant Tea weigh 100, and Herbal Salve 25, like the base game's salves.
- The mod description now lists the 1.14 wild finds, Iced Teas, Shaved Ice and fire-roasted peanuts.
- Modders: the pickle vat gate, Spice Brine and the Dig for Truffles injection now log one warning per cause when a game update changes a field they read.

## [1.14.3] - 2026-10-13

- **Mushroom Basket perk: the basket and mushrooms land properly.** The base game does not let a Hand Basket be equipped, so the basket dropped on the ground anyway. The perk now hands over a quality 50 Hand Basket and 16 mushrooms (2 each of 8 kinds) beside you, ready to put in the basket yourself.

## [1.14.2] - 2026-10-12

- **Raw Peanuts roast in a lit fire, like vanilla Pine Nuts.** In a campfire, fire pit or fireplace they become Roasted Peanuts in about 15 minutes, so the 2-hour Roast button is gone. Left in the fire about 4 hours, Roasted Peanuts char into Charred Remains. The peanut help text lists every use.
- **Card art in the vanilla form.** 175 pictures are cropped to the subject and saved smaller, so cards fill like vanilla items and the pictures take about a sixth of the space.
- **Liquid pictures.** All 27 liquids show the base game's clay bowl filled with that liquid's colour, replacing water drops, tea cups and one ale picture shared by eight drinks.
- **Closer borrowed pictures for seven cards.** Berry Pulp uses Bilberry Pulp, Elderberries and Dried Elderberries the blackcurrant pictures, Dried Horseradish the Rye Flour heap, Roasted Chestnuts and Roasted Hazelnuts the raw nut pictures, and Cooked Watercress Nettle Mash.
- **Cleaner Gruit Bundle and Dried Wild Flowers pictures.** The white fringe and gaps between leaves are gone.
- **New pictures for 43 cards:** Bark Meal, Berry Leather, Camas Cake, the four doughs (they showed a bar of soap), Carrot, Chicory Root, Crabapples and Dried Crabapples, fresh and dried Elderflowers, Fiddleheads, Salted Fish Mash, raw and cooked Fishcake, Ful, the three grow bag stages, Hops, Sliced Horseradish, Kale, Leek, Mandrake Root, Mustard Seeds, Dried Wild Pears, Rhubarb, and painted beds for the carrot, cress, hop, kale, leek, moss and rhubarb crops.

### Added

Numbers for the new foods (chances, food values, spoilage, growth times) are starting values and may be tuned.

- **Horseradish Root** (spring and autumn, Pine Clearing and Alder Woods). Cut it into Sliced Horseradish to season a plateable food with a new Horseradish spice, or dry it into Dried Horseradish, which seasons three plates.
- **Chicory Root** (summer and autumn, Highland Meadow and Foraging Path). Roast it, then steep it in hot water for Chicory Brew, whose Drink quenches thirst and gives **Chicory Lift**: you tire more slowly and stress rises a little faster, doubled from the second cup.
- **Goosefoot Greens** (spring and summer, Foraging Path and Oak Clearing). Eat them for **Goosefoot Purge**, a gentle parasite cure beside Death Cap Tincture's hard one.
- **Water Lily Rhizome** (spring to autumn at every river place) must be cooked by a fire before you eat it. **Turnroot Greens** come about half the time when you harvest a mature turnroot field by hand, not with a sickle, and are eaten raw. Both go into a stew.
- **Crabapples, Wild Pears and Wild Plums** at the mod's clearings. Eat each fresh, dry it for long keeping, or stew it. The new Cooking-tab **Plum Preserve** takes 3 Wild Plums and 2 Honeycomb.
- **Chestnuts and Hazelnuts** (autumn). Eat them raw or roast them. Grind a Roasted Chestnut into Chestnut Flour, soak it into dough and bake Chestnut Flatbread.
- **Sloes** (autumn). Crush them into water for Sloe Must, which ferments into **Sloe Wine** in four days.
- **Elderflowers and Elderberries.** Dry the flowers and steep them in hot water for **Elderflower Cordial**. The berries are eaten, dried or stewed.
- **Fiddleheads, Thistle Greens and Camas Bulb** (spring and summer). Raw Fiddleheads give short food poisoning, so cook them. Camas roasts, and a Camas Cake grinds into Camas Flour that soaks into dough and bakes into Camas Flatbread.
- **Mandrake Root, Rhubarb and Mustard Seeds.** Steep Mandrake in hot water for a sleepy Mandrake Draught (vanilla Fairy Tea's effect). Rhubarb is eaten, stewed, preserved (**Rhubarb Preserve**) or planted (**Plant Rhubarb**). Mustard Seeds season plateable food with a new Mustard spice.
- **Kale, Carrot and Leek** are found foraging from spring to autumn. Eat or stew them, or plant them with **Plant Kale**, **Plant Carrot** and **Plant Leek** (2 of the vegetable and a hoe).
- **Hops, Grapes and Oats** are found foraging in summer and autumn. Steep Hops in finished rye ale for **Hopped Ale**, crush Grapes into Must for **Grape Wine**, and grind Oats into Oatmeal for porridge. Plant them with **Plant Hops**, **Plant Grapes** and **Oat Field**.
- **Broad Beans** are found in spring and summer, eaten or stewed, and grown in the **Broad Bean Field**.
- **Pea Flour, Turnroot Flour and Ful.** Split Peas and Turnroot Chips grind into flours that soak into dough. Turnroot Dough bakes into Turnroot Flatbread, Pea Dough into Pea Bread. **Ful** is 3 Broad Beans and 1 Peanut Oil.
- **Sprouted Rye Bread and Nettle Bread** are new Cooking-tab breads on the vanilla doughs.
- **Nut Milk and Nut Tofu.** Soak Peanut Meal in cool water for Nut Milk, which sours in about three days. Curdle it with Salt into Nut Tofu.
- **Tendon Soup, Fishcake and Haggis.** Three new meat and fish dishes.
- **Liver Sausage.** A raw sausage you cook, boil or dry into long-keeping Dried Liver Sausage. The raw one risks parasites, as vanilla's does.
- **Fish Sauce and Lutefisk.** Salted Fish Mash dragged onto water becomes Young Fish Sauce, then Fish Sauce after about 5 days. Fish soaked in Lye for about 2 days, rinsed and cooked becomes Lutefisk.
- **Bark Meal.** A famine food made from 2 Oak Bark. It gives a little food and costs a little Stress and Gratification.
- **Iced Teas and Shaved Ice, the first cold foods.** Drag a vanilla Ice Block onto Chamomile or Dandelion Tea (**Chill with Ice**), or make Shaved Ice from an Ice Block and a Berry Preserve. Drinking or eating them gives **Cooled**, which lowers Perceived Temperature. An Ice Block melts in about 2 hours.
- **Vigor Elixir.** Drag Ground Ginseng onto Reishi Tea held in a Clay or Copper Cauldron for a strong Herbal Tonic dose (72 of 96). It is refused outside a cauldron.
- **Herbal Ferment.** Drag a honeycomb onto Chaga, Ginseng, Lion's Mane, Reishi or Yarrow Tea. After about three days it becomes a drink that quenches thirst and gives a mild Herbal Tonic.
- **Herb-spiced wines and ales, and Needle Ale.** Dried Chamomile, Yarrow or Ginger dropped on finished wine or ale makes a Herb-Spiced version. A Needle Bundle steeped in rye ale makes Needle Ale.
- **Mossbed.** Make Mossbed (2 Healer's Moss, 1 Moss) grows 3 to 4 Healer's Moss per Harvest after about 5 days, a camp source for a card vanilla gives only from foraging.
- **Grow Bags for Golden Oyster and King Oyster.** A smaller alternative to a log: 1 wood shavings, 5 mushrooms and 1 Small Cloth. It colonizes in about 2.5 days and gives 12 mushrooms.
- **Spore Prints.** Harvesting a ready mushroom log also gives a Spore Print, and six new blueprints start a log from 1 print instead of 10 mushrooms.
- **Dried Death Cap and Toxin Tolerance.** Dry a Death Cap on the Drying Tray, then **Taste a Sliver** for mild food poisoning (+4) and a fading Toxin Tolerance that makes later food poisoning clear faster.
- **Unknown Chanterelle.** A quarter of chanterelle finds are now Unknown Chanterelles, in the same places and seasons. Identify one with Herbalism 25 or more, with the Botanical Field Guide, or, with Community Mod Chest, after the Academy's Herbalism course. It becomes a Chanterelle or an inedible False Chanterelle. Eating it unidentified gives food poisoning (+8).
- **Velvet Shank, the first winter-only forage.** Oak, alder and willow woods can turn one up in winter (14% a forage). It cooks and dries like Golden Oyster.
- **Old-Growth Grove.** A seventh location west of Misty Falls, opening once you have explored all six clearings. Foraging there can find Truffles, Morels and Cloudberries.
- **Watercress and the Cress Bed.** Riverside forage from spring to autumn has a 10% chance of Watercress. The **Cress Bed** blueprint can be built only at a river and regrows about every 3 days.
- **Fungal Bloom.** A rare autumn event after heavy rain: for about 2 days, arriving at each of the six clearings leaves a patch you can harvest for mushrooms before it rots.
- **Forage hotspots.** A Lingonberry Patch at Oak Clearing, a Cloudberry Patch at Pine Clearing and a Chamomile Patch at Alder Woods stay at their place. **Pick** uses a quarter of a patch, and a picked-clean patch rests about 7 days.
- **Forager's Notebook.** Made from 3 Hemp Paper and Twine. It records each wild plant or mushroom the first time one turns up, under the place you found it.
- **Seasonal flushes at the mod's forage spots.** Wild Flowers and Common Plantain are twice as likely in spring at the Foraging Path and Alder Woods, and Chanterelle and Black Trumpet in autumn at the Highland Meadow. Other seasons keep their chance.
- **Berry Leather and honeyed sweets.** Four Mash blueprints turn berries into Berry Pulp, which dries into Berry Leather. With Honeycomb, make Honeyed Nuts, Honeyed Pine Nuts and Honeyed Berries.
- **Spiced Pickle Brine and Spiced Pickles.** Drag Dried Wild Garlic or Dried Ginger onto a vat of plain brine, then pick one of four new Spiced Pickle blueprints. The plain pickles now accept only plain brine.
- **Herb Butter and Herb Cheese.** Drag butter or cheese onto Black Trumpet, Shiitake or King Oyster Powder or Dried Ginger. The spread is plain food that keeps much longer than plain dairy.
- **Wax Leather Dressing.** Drag Linseed Oil onto Beeswax, then the dressing onto a leather garment to raise its Quality by 5.
- **Medicinal herbs carry their effect into a stew.** Adding Reishi, Lion's Mane, Chaga, Ginseng or Yarrow to a pot of hot water gives a gentle share of Herbal Tonic (or Herbal Analgesic and Clotting for Yarrow), below a tea's dose.
- **Allied Partners eat this mod's food.** A vanilla Partner takes the Eat of 107 foods and the Drink of 18 drinks (the plain teas and broths, Tendon Soup, Nut Milk, the ales and wines). It never touches medicines, anything that cures or causes poisoning or infection, anything with a timed effect, Sleep and Stimulant Teas, Fish Sauce or the ready pickle vats. 25 foods gained the human food tag and 7 drinks the human drink tag; with the framework's opt-in Wildlife Raids on, those foods can be raided from open storage like vanilla food.
- **Fresh Chamomile, Dandelion and Wild Flowers are flowers.** With Community Mod Chest, each fills its Painted Clay Vase (Arrange Flowers). Dried herbs stay off it.
- **Lanolin in the Herbal Salve.** With Sirus23's Mod Collection, the salve's oil slot also takes Lanolin. Without it nothing changes.
- **Wound remedies for companions.** Dried and Ground Yarrow, Yarrow Tea, Herbal Salve and Plantain Poultice can treat a companion's wounds with Sirus23's Mod Collection. Reishi Tea is left out.

### Fixed

- **The Botanical Field Guide sends you to the right places.** It had listed Lion's Mane and Black Trumpet in cave locations with no Forage action, said Truffles and Peanut Pods could be dug anywhere, and put Dig for Truffles in the mod's own oak and alder places. All corrected in English and Chinese.
- **The Botanical Field Guide lists every plant the mod adds.** The wild finds, fruit and nuts, crops and Velvet Shank each now have a line with seasons and places, and the Old-Growth Grove is listed as a place.
- **Felled trees grow back at the Foraging Path, Pine Clearing, Oak Clearing and Alder Woods.** Those clearings had stripped their own tree-regrowth actions. Each now replants one tree per species, as vanilla does; a large oak or pine waits while a large birch or willow still stands.
- **Dried Yarrow could not appear since the herb-spiced wines were added.** Its five Spice drags were in the wrong list of the card, which broke creating any Dried Yarrow. They are ordinary drags again.
- **The Lutefisk lye-soak blueprints did nothing when you pressed Build.** The lye slot now asks for a container holding 100 Lye, like vanilla Soap.

### Changed

- **ForageDropDensityScale now reaches every H&F forage and dig drop.** It used to scale only drops added by code, so it missed the mod's own locations, the birch-tree rows, Chaga, Watercress and the Old-Growth Grove. It now covers every Forage, Clear, Dig up Mud, Dig up Dirt and Dig for Truffles drop of this mod's cards, including its herbs another mod adds, and scales a seasonal flush with the base chance. At the default 1.0 nothing changes; an existing .cfg keeps its value.
- **Vanilla-location forage is now declared data, with nothing a player sees changed.** The same cards, chances and seasons appear at the same places. A location a future game update adds is no longer picked up by name and needs an entry. The old code also held branches for the caves and the mod's own locations that never fired, so the README and Player Guide no longer claim cave forage or Dig for Truffles at the mod's own locations.
- **Cleaner card art.** White left by background removal is gone from 21 pictures, including the Chamomile and Dandelion plants, several teas, the herbal oils, Herbal Salve, Herbal Tincture, Pain Relief Powder, Peanut Oil and both Wild Berry Pasties.
- **Docs.** The README and Player Guide now say which picture each card shows, the mod description counts 9 foraged berries and wild fruits (it said 4) and names the Plum Preserve, Chestnuts, Hazelnuts and Elderflowers, and the modder patch list includes the Forager's Notebook and Academy Herbalism patches.

## [1.14.1] - 2026-09-28

Thanks to Chiwei, whose report is behind this release.

### Added

- **Hemp Seed, Peanut and Linseed Oil season food.** Drag the oil onto a plateable food (cooked meat, fish, eggs and the like) with **Add** for its flavour and a little extra nourishment. One Add uses the whole bowl and gives back the Clay Bowl.
- **Plant Chamomile, Plant Plantain and Plant Dandelion** (Farming > Agriculture). Each unlocks once you hold the herb and takes 2 of it and a hoe. It grows for 5 days and gives 3 to 4 back, training Herbalism.
- **Ripe Puffball, a spore styptic.** A Giant Puffball left uneaten until its spoilage fills ripens instead of rotting. **Dust a Wound** raises Yarrow Clotting by 24 (half of Ground Yarrow's), slowing bleeding. It gives 3 dustings and rots after twice the fresh puffball's time.
- **Forager's pasties.** Drag a foraged mushroom or berry onto greased rye or wheat dough in daylight (**Fill a Pasty**), then bake the raw pasty in an oven. The mushroom pasty takes Black Trumpet, Chanterelle, Chicken of the Woods, Golden Oyster, King Oyster, Lion's Mane, Morel, Shiitake and Giant Puffball. The berry pasty takes Blackcurrant, Redcurrant, Lingonberry and Cloudberry. Food values are copied from vanilla's pasties.
- **Wild Bees' Nest, a honeycomb source.** The game places no vanilla beehive, so Honeycomb (and the Berry Preserve) had no source. A nest now sits at the Foraging Path, Pine Clearing, Oak Clearing and Alder Woods. Smoke it with a Lit Torch (or lit Bee Smoker) and **Take the Comb** for 1 to 2 Honeycomb without stings. Unsmoked, you get stung. The comb grows back in about 7 days, not in winter.
- **Herbal Honey Syrup.** Honeycomb, two Dried Chamomile or two Dried Common Plantain and a Clay Bowl make syrup in the Medical tab. **Take a Spoonful** raises the Heather Effect, which slows chest and throat infections and eases Stress. It gives 3 spoonfuls and never spoils.
- **Gruit Ale.** Steep a Gruit Bundle (Dried Bog Myrtle, Dried Heather Flowers, Dried Yarrow and Twine) in finished rye ale (**Steep in Ale**) for a bitter, herbal ale that spoils half as fast. It does not freeze or age in a barrel.
- **Hemp paper and the Botanical Field Guide** (Support tab). Hemp Stalks make Hemp Pulp, which soaks for about a day and presses into Hemp Paper. 6 paper and Twine bind the Guide, whose **Read** action lists every plant the mod adds with its seasons and places.
- **Forager's Seasoning.** Black Trumpet, Shiitake and King Oyster Powder plus Salt make a pouch of 3 pinches. **Add** it to a plateable food for Earthy and Savoury flavour.
- **Herbal Bath Soak.** Two Dried Chamomile, a Dried Yarrow and Twine. Dropped on hot water in a pot, **Herbal Steam** eases stress for about half a day. With Advanced Copper Tools 1.18.0 or later, **Soak in the Tub** on a filled Copper Bathtub lasts about a day. It does not wash you.
- **The Death Cap Ward is back.** Lash 5 Death Caps to 2 long sticks and place it at a location. While you are there, bear, wolf, wolf pack, primeval wolf and boar attacks, and other wild-animal encounters that start without a creature on the map, are turned away 45% of the time. It never stops people, your own fights, or other mods' animals. Pick Up moves it and Dismantle returns the sticks.
- **Linseed Cake.** Pressing Flax Seeds also gives a Linseed Cake. **Add to Fodder** on a placed Dry Fodder gives it 6 Amount and 25 Nutrition. It keeps about 30 days.
- **Maggots clean an infected wound.** Rotten Remains gain **Collect Maggots** (outside winter). **Apply Maggots** on an infected open wound lowers Infection by 180 and raises Stress by 15.
- **Pine Nut Oil.** **Press Pine Nuts** on the Oil Press makes a cooking oil. Eat it or **Add** it to a plateable food. With Advanced Copper Tools 1.17.0 or later it also refuels its lanterns and braziers.

### Changed

- **The Apothecary Shelf takes Wild Ginseng and Wild Flowers, and no longer takes the Herbal Incense Bundle.** Both now count as herbs. A bundle already on a shelf stays. Wild Ginger stays a spice, and the shelf still refuses it.
- **Making medicines and harvesting plants and logs trains Herbalism.** Crafting a Herbal Salve, Plantain Poultice, Herbal Tincture, Healer's Moss Tincture, Death Cap Tincture, Pain Relief Powder, Sleep Tea, Stimulant Tea or Anti-Nausea Tea, and harvesting Mature Ginseng, Flowering Yarrow or any of the six ready mushroom logs, now gives Herbalism. Mix into Hot Water brews give none.
- **Raw Morel and raw Chicken of the Woods give food poisoning.** Eating either raw adds 8, which fades by itself in about two in-game hours with a little nausea. The Morel keeps its own nausea. Cooked ones are unchanged.

### Fixed

- **The Drying Stack could be researched with no Drying Tray.** It now needs one on the board, as its unlock text says.
- **Anti-Nausea Tea and Stimulant Tea can be unlocked.** Their unlock condition never completed. Your first Dried Ginger now unlocks Anti-Nausea Tea, and your first Ground Ginseng unlocks Stimulant Tea.
- **Trait starting items are usable.** Items from Edibles Kit, Mushroom Basket, Seed Bag, Smoke Kit and Hemp Farmer that started at quality 0 now start at 50 (Fibers at 30). Needs CSFF Mod Framework 2.28.0.
- **Dismantling returns usable materials.** The Apothecary Shelf, Drying Stack and Wooden Pantry give back oak Planks, and a mushroom log gives back an oak log, instead of wood of no kind.
- **Birch woods give Blackcurrant, Redcurrant, Common Plantain, Chamomile and Chanterelle.** The old code looked for a location name no vanilla location has, so none dropped. They now drop at the 17 forest spots with a large birch tree. Blackcurrant rises from 5 to 12% at five oak and alder spots and from 8 to 12% at four river spots; where a spot already gave a card, nothing drops less.
- **The README and Player Guide say what the mod does.** Truffles come from Dig for Truffles at oak and alder locations, never under birch. The Death Cap's Apply Poison is listed. The Oil Press table lists all eleven pressings, and every pressed oil refuels Advanced Copper Tools' Metal Lantern, Copper Brazier and Copper Oil Flask (ACT 1.17.0 or later).
- **Stimulant Tea and Sleep Tea say what they do.** Stimulant Tea lowers Stress by 12, and Sleep Tea lowers it by 20 and fills you a little. Neither touches sleep, focus or energy. Text corrected in English and Chinese; the teas themselves are unchanged.
- **Every Add to Stew ingredient goes into the stew.** On 62 cards the action never reached the stew: in a vanilla Cooking Pot the card was only put in the pot, and in the Copper Cauldron it vanished. Now a mushroom, berry, herb, root, powder or Truffle Butter dropped on a Cooking Pot, Clay Cauldron, Clay Bowl or Copper Cauldron holding hot water (100 or more, at least 290 units, not at full thickness) is stirred in, adding vanilla's numbers for the nearest ingredient and its flavours. Cold, too thick or too little water says so. A full Clay Bowl of hot water works too.

---

## [1.14.0] - 2026-09-27: The Physic Garden

### Added

- **Chaga, Dried Chaga, Ground Chaga and Chaga Tea.** Chaga is a birch conk found foraging in Alder Woods (likeliest), Oak Clearing, Pine Clearing, Highland Meadow and vanilla forests with large birch trees, in every season. Dry it, grind it and mix the powder into hot water for the mod's biggest Herbal Tonic dose, about half a day.
- **Plant Ginseng, Plant Yarrow and Plant Peanuts** (Farming > Agriculture). Build with 2 of the herb (or 2 Raw Peanuts) and a hoe. Ginseng grows 14 days, Yarrow 5, Peanuts 10; the harvest gives 3 to 4 herbs or 3 to 5 Peanut Pods. Roasted peanuts do not sprout.
- **Spent mushroom logs give back wood.** A ready log still gives 20 mushrooms and now also 2 to 3 Wood Shavings and a piece of Firewood, so the shavings start your next log.

### Changed

- **Balance: medicinal teas dose you once, when you drink them.** Mixing Ground Ginseng, Lion's Mane, Reishi or Yarrow into hot water used to apply the effects on the spot and again on drinking. Brewing now does nothing and the Drink gives the effect once, so a cup is worth about half the Wellbeing and Stress relief it used to be.
- **Balance: immune boosts now fade.** Ginseng, lion's mane, reishi, shiitake, black trumpet, king oyster, truffle oil, their teas and Reishi Broth used to raise your immune system permanently, so it stacked for the whole run. They now give a **Herbal Tonic** status that lasts a few hours (up to a day when doses stack), like the game's garlic, and doses close together give Strong Herbal Tonic. Immune strength already banked in a save is kept.
- **Death Cap Tincture no longer adds lasting pain.** It still clears gut parasites and makes you sick for a while, but repeated doses no longer pin you in Minor Pain for the rest of the run.
- **Herbs, mushrooms and berries follow the seasons at the mod's own forage spots.** The Foraging Path, Oak Clearing, Alder Woods, Pine Clearing and Highland Meadow gave every herb and mushroom all year. Now ginseng, ginger, yarrow, wild flowers, plantain, chamomile, puffball, cloudberry, death cap, chanterelle, shiitake and black trumpet run spring to autumn, lingonberry summer and autumn, and blackcurrant summer. Morels and Chaga stay year-round. Needs CSFFModFramework 2.27.0 or later; with an older one these drops stay year-round.

### Fixed

- **Season text matches where the mushrooms grow.** Golden Oyster, King Oyster and Morel are all-year finds, Lion's Mane and Cloudberry grow spring to autumn, Chanterelle's text now includes spring, and the starting-fungi perk no longer gives mushrooms a season they do not keep. Cloudberry's help now includes the mod's own forage spots, and King Oyster's Chinese help text is complete.
- **The README and Player Guide no longer say hemp can be planted into vanilla garden plots.** Plant Hemp Seeds and Hemp Field are how hemp is planted.
- **The README and Player Guide no longer call raw Chicken of the Woods toxic.** (1.14.1 later made raw Chicken of the Woods give short food poisoning.)
- **README corrections.** The Oil Press list names all 11 oils, and Hemp Farming says what a harvest gives.
- **Chinese help text for Ginseng Tea, Reishi Tea and Truffle Oil** is now a proper Chinese preparation and benefits text. It had repeated the English line and named the wrong item.

## [1.13.7] - 2026-09-23

### Fixed

- **Dismantling a Drying Stack or a Drying Tray no longer destroys what is on it.** Taking either
  apart with trays or herbs still on it deleted them along with the frame. They are now set down
  beside you, the same way the Wooden Pantry, the Apothecary Shelf and the game's own drying rack
  already empty when they are taken apart.
- **Peanut Oil and Linseed Oil no longer claim they can be rendered into lamp oil.** No recipe and
  no lamp takes either oil, so that use is gone from both cards (English and Chinese) and from the
  README. Hemp Seed Oil keeps the line, because Advanced Copper Tools' Render Hemp Seed Oil recipe
  really does turn it into lamp oil.

## [1.13.6] - 2026-09-23

### Fixed

- **The ground King Oyster card no longer has a hole in the middle of its mushroom pile.** The
  card art's white background had been cut away through a gap in the drawing's outline, so the
  board showed through the centre of the pile. The picture is repaired.

## [1.13.5] - 2026-09-19

### Fixed

- **An emptied Pickle Vat gives back the fired vat and the Open Pickle Jar.** Eating the last of a
  Ready Pickle Vat's five servings, or leaving it until the pickles spoil, removed the vat and
  returned nothing, so the fired vat you built was lost every time. The return was set up in 1.10.18
  but the game only hands back a destroyed card's leftovers when the removing action asks it to, and
  these two never did. Both now do, for all four flavours. The README no longer says a vat can be
  broken up mid-ferment: no action in the game does that.

## [1.13.4] - 2026-09-18

### Changed

- **Truffle Butter, Hemp Stalks and the Apothecary Shelf have their own pictures.** They were
  borrowing the game's Butter, Nettle Stems and Shelf pictures. Truffle Butter is now a pat of
  butter flecked with dark truffle, Hemp Stalks a tied bundle of long fibrous stalks, and the
  Apothecary Shelf (the item, the placed shelf and its blueprint) a narrow slatted shelf holding
  herb bundles, mushrooms and clay jars.

## [1.13.3] - 2026-09-18

### Changed

- **Finished art for five cards.** Black Trumpet Powder, King Oyster Powder, Shiitake Powder and
  the Smouldering Incense Bundle were showing a plain white placeholder, and Truffle Salt borrowed
  Shiitake Powder's. Each now has its own illustration. Art only, no gameplay change.

## [1.13.2] - 2026-09-16

### Changed

- **Download cut from 54.4 MB to 27.4 MB.** The build script wrote a second, byte-identical copy of
  every card image into `Resource/Texture2D/` alongside `Resource/Picture/`, which nothing ever
  read, so every release archive carried the whole art set twice. Fixed in packaging; no gameplay
  or content change.

### Fixed

- **Press Plate could not be built.** The Press Plate blueprint asked for a tool from a tab group
  named GpTag_SharpAxe, which does not exist in the game, so the requirement slot showed the raw
  "GpTag_SharpAxe" text and no tool could ever fill it. It now asks for the game's own sharp
  shaping-tool group (Flint Axe, Metal Axe or Metal Handsaw), matching the "sharp axe" the
  description already promised. From TheFifthLorax's 2026-09-15 report.

## [1.13.1] - 2026-09-16

### Fixed

- **Medicine pain and bleeding relief.** Reishi, Dried Reishi, Plantain Poultice, Herbal Salve, Pain
  Relief Powder, Healer's Moss Tincture, Yarrow Tea, Yarrow, Dried Yarrow and Ground Yarrow all used
  to lower Pain's base directly, and the three yarrow forms plus Yarrow Tea also lowered BloodLoss's
  base directly; neither stat has a base source in this game (base rate 0, nothing else raises
  either), so every one of those reductions clamped to nothing with no error. Each pain item now
  raises the base of a new hidden Herbal Analgesic stat, and the yarrow items and Yarrow Tea a new
  hidden Yarrow Clotting stat, both carrying their relief through a decaying status the way vanilla
  medicines work. Ground Yarrow's "Mix into Hot Water" step no longer doses the player directly; the
  tea it brews doses instead. Item help text was corrected where it claimed effects (fever, faster
  healing, infection resistance) that never existed. Investigation prompted by Chiwei's
  2026-09-15 report.

### Fixed (packaging)

- **GameStat folder was never deployed.** `Herbs_And_Fungi.csproj` had no `<Content Include>` for
  `GameStat/*.json`, so the mod's two quest-state stats never reached `bin/Release` on any build,
  and the two new medicine stats above would have shipped the same way. The csproj now includes the
  folder on every deploy path.


## [1.13.0] - 2026-09-08

The last row of the mod's Audit Remediation Plan, and a mod-wide repair to the flavour data that
reading it exposed. Neither has been confirmed at a running game: both carry rows in
`.claude/playthrough-test-status.json` (T2.230, T2.231) awaiting human verification.

### Added

- **Truffle Butter.** Drag Fat, a Fat Chunk, a Butter Chunk or Milk Butter onto a fresh Truffle (or
  the truffle onto the fat) and the fat is used up and the truffle becomes a block of Truffle Butter:
  Strong Earthy, Strong Savoury, Medium Buttery. Eat it as it is, or add it to any stew. Keeps about
  a week. Uses the vanilla butter sprite. (T2.230)
- **Truffle Salt.** Drag Salt onto a whole Dried Truffle (or the truffle onto the salt) and grind
  them together for a quarter hour; the salt is used up and the truffle becomes a finishing salt that
  never spoils: Strong Earthy, Strong Savoury, Subtle Salty (the same Salty as plain salt). Add it to
  any stew. Art is a placeholder (reuses the Shiitake Powder sprite). (T2.230)

### Fixed

- **Every flavour this mod declared was mostly inert, and the strongest ones were the deadest.**
  `FlavourTags[].Intensity` is an engine enum: 0 = Medium, 1 = Strong, 2 = Subtle. This mod's 72
  flavoured cards were authored on an ordinal 1 to 7 scale instead. The game reads the raw number
  through a switch whose default arm returns zero, so the 91 entries carrying 3 to 7 (the truffles,
  the dried and cooked mushrooms, the concentrated berries, everything the scale meant as "strong")
  contributed no flavour at all and showed no flavour line on the card, while a 1 (meant as a trace)
  read as Strong and a 2 as Subtle. Remapped in place: 1 and 2 to Subtle, 3 and 4 to Medium, 5 to 7
  to Strong. Three of the four spice tags already used the enum and are unchanged; Peanut Oil's did
  not and was remapped the same way. Net effect in a stew: this mod's ingredients now contribute
  flavour where before most contributed nothing, so flavour scores on stews built from them will
  change. Enforced by `Development_Tools/Tests/FlavourTags-IntensityEnum.Tests.ps1`, which is
  self-demonstrating and sweeps every mod. (T2.231)

### Notes

- **The row that produced Truffle Butter was marked BLOCKED for two waves on a premise that did not
  hold.** Its prompt said to clone the Hemp Butter heat-activate-then-solidify chain and wait for
  that chain's in-game confirmation (T2.153, still unrun). Truffle butter is a compound butter: fat
  plus truffle, no activation, no drying timer. The shape actually cloned is the mod's own "Mix with
  Fat or Butter" drag action, which is the working entry step of that same chain. T2.153 remains
  open on its own merits and no longer gates anything.
- **Truffle Salt deliberately does not require a mortar on the board.** The engine field for that
  (`RequiredTagsOnBoard` on a card action) is used by no vanilla card action and no mod in this
  repo, so it cannot be checked offline, and a mis-shaped condition would hide the action forever
  rather than merely skip the requirement. The grind is a hand action costing one time unit.
- **Both cards' nutrition, trading values and flavour strengths are provisional and have not been
  balance-tested.** The flavour remap's tercile thresholds are a judgment call recorded here so the
  next reader does not re-derive them: they reproduce vanilla's own skew (Subtle most common, Strong
  rare), and the one spice tag that was already enum-valued (`Spice_HempOil`: Nutty Medium, Earthy
  Subtle) agrees with what the remap produces from its own card's ordinal values (Nutty 3, Earthy
  2), which is the closest thing to a second opinion the data offers.


## [1.12.0] - 2026-09-08

The last two buildable rows of the mod's Audit Remediation Plan. Neither has been confirmed at a
running game: both carry rows in `.claude/playthrough-test-status.json` (T2.225, T2.226) awaiting
human verification.

### Added

- **Apothecary Shelf**, a filtered store for the mod's own materia medica. It takes herbs, fungi,
  powders and medicines and refuses ordinary food and building materials, and it slows spoilage on
  what it holds to 60% of the normal rate, against the Wooden Pantry's 75%. It is the narrower,
  cheaper counterpart to that pantry: 3 planks, 2 twine and a hammer, unlocked once you have dried
  yarrow, and it appears in Construction > Furniture. Reuses the vanilla shelf sprite. (T2.225)
- **Herbal Incense Bundle**, the mod's first ambient effect and the room-scale counterpart to the
  personal Herb Pipe. Bind 2 dried wild flowers, 1 dried chamomile, 1 dried yarrow and 1 twine, then
  touch a flame to it. While it smoulders it eases Stress and raises Sanctuary for you anywhere on
  that board, rather than only in your hands, and it burns down over about three hours and is gone.
  Snuff it out to keep the remainder. Appears in Survival > Support. The lit card's art is a
  placeholder. (T2.226)

### Notes

- **The plan's own corrected guidance for the shelf was wrong, and the fix is recorded here because
  the plan row it corrected has now been pruned.** That row told the implementer to filter on
  `tag_Herb` plus `tag_Preservable`. `CardFilter.SupportsCard` treats positive `TagFilters` as OR,
  not AND, so that pair would have accepted every one of the 103 mod cards carrying
  `tag_Preservable` and produced a container that filters nothing. The shipped filter is
  `tag_Herb`, `tag_Fungus`, `tag_Powder`, `tag_Medicine`, which reaches 56 of this mod's cards.
- **Both magnitudes are provisional and have not been balance-tested**: the shelf's 0.6 spoilage
  multiplier, and the incense's -0.3 Stress rate, +20 Sanctuary and 12-unit burn time.
- Truffle Butter and Truffle Salt remain BLOCKED, unchanged from 1.11.0. Their prerequisite is an
  in-game confirmation of the Hemp Butter activation chain, tracked as T2.153, which is still
  unrun.

## [1.11.0] - 2026-09-08

Six new features from the mod's Audit Remediation Plan, plus one silent-failure fix. None of the
below has been confirmed at a running game yet: every item carries a row in
`.claude/playthrough-test-status.json` (T2.188, T2.189, T2.208-T2.211) awaiting human verification.

### Added

- **Three more pressed oils: Yarrow, Chamomile and Ginseng.** The Oil Press went from 8 oils to 11.
  Each presses from the FRESH herb plus a Clay Bowl, matching the four herbal oils already shipped,
  and each carries `tag_Oil` so it joins the lamp-fuel pool. (T2.188)
- **Linseed Oil**, pressed from 3 Flax Seeds plus a Clay Bowl. The Seed Bag perk has always granted
  flax seeds that no recipe in this mod could use; this is their first use. Its description mentions
  the traditional wood-treating use as flavour only: there is no wood-finishing mechanic. (T2.208)
- **Herbalist's Advantage**, a Situational character-creation trait costing 30 Suns that starts you
  with a Herbalism head start. This is the mod's first perk that biases a SKILL rather than granting
  items; all 15 previous perks only handed you objects. (T2.189)
- **Three culinary mushroom seasoning powders**: Black Trumpet, Shiitake and King Oyster. Grind the
  dried mushroom with a mortar and pestle. Until now the mod ground only medicinal species. Each
  powder carries Savoury and Earthy flavour. Art is placeholder. (T2.209)
- **Three mushroom broths with distinct effects**, beside the existing plain Mushroom Broth: Reishi
  (immune and stress), Lion's Mane (focus) and Chanterelle (morale), each brewed from its own dried
  mushroom. All six magnitudes are provisional and have not been balance-tested. (T2.210)
- **Berry Preserve**, the mod's first concentrated-sugar preservation path: four dried berries plus
  honeycomb, slow-simmered into something that keeps far longer than the fruit it came from. (T2.211)

### Fixed

- **Peanut Oil's spice effect never did anything.** The card referenced a spice tag
  (`herbs_fungi_spice_peanut_oil`) that had no backing file, so the reference resolved to nothing and
  the oil silently contributed no flavour or stat effect when cooked with. It was the only one of the
  mod's four spice-tag references that did not resolve. The tag now exists and carries Peanut Oil's
  own flavour profile (Nutty, Earthy, Grassy) plus the same nutrition values its sibling oils use.

## [1.10.18] - 2026-09-05

### Fixed

- **Destroying or harvesting any Pickle Vat (Ready or Sealed, all 4 flavors) returned nothing, the
  vat and its Open Pickle Jar just vanished.** All 8 variant files (`PickleVatReady_{Frogs,Meat,
  Mushrooms,Vegetables}.json`, `PickleVatSealed_{Frogs,Meat,Mushrooms,Vegetables}.json`) authored
  `DroppedOnDestroy` as a flat array of drop entries, but the real field type is a nested collection
  (the same shape `ProducedCards` uses elsewhere in this mod) with the payload one level deeper. The
  mismatched shape deserialized without error, so `DroppedCards` stayed null on every collection and
  nothing spawned, no log, no exception, just an empty destroy. Reshaped all 8 files to the correct
  nested `CollectionName`/`CollectionWeight`/`DroppedCards[]` structure; destroying or emptying a
  Pickle Vat now correctly returns the reusable fired vat plus an Open Pickle Jar, as the README's
  Pickle Vat walkthrough has always described. First identified in the 2026-09-01 audit, fixed here.
  Not yet verified in-game, recommend adding to `/playthrough-test-plan`.

- **"Return Bowl" on an Open Pickle Jar produced no clay bowl, and destroying the jar dropped
  nothing.** The same flat-vs-nested shape bug also affected `CardData/Item/OpenPickleJar.json`, on
  *both* its `DroppedOnDestroy` and its "Return Bowl" `ProducedCards`. It was missed by the sweep
  above because the jar is a CT0 item rather than one of the CT2 vat structure cards, so the
  structure-scoped repair never touched it. With both arrays reshaped to the nested
  `CollectionName`/`CollectionWeight`/`DroppedCards[]` structure, "Return Bowl" now returns the clay
  bowl lid, making the README's "reclaim the clay bowl lid and reduce per-batch clay cost" behavior
  real. Not yet verified in-game.

### Changed

- **Seasonal forage drops are now actually gated by season.** The forage-drop injector's per-item
  season hints ("Summer only", "Spring/Summer/Fall only", "Late Summer/Fall") were previously dead
  parameters, every herb, mushroom, and berry dropped year-round regardless of the comment. Each
  seasonal injected drop now carries a `StatsModifiers` entry keyed on the vanilla
  `SeasonCounter_<season>` GameStats that drives its chance to 0% outside its allowed seasons:
  Blackcurrant and Redcurrant are Summer-only, Lingonberry is Summer/Autumn, and every other
  seasonal herb/mushroom is Spring/Summer/Autumn (absent in Winter). Morels, King/Golden Oyster,
  and dug-for Truffles remain year-round. The suppressor is added only to the mod's own injected
  DropChance, never a vanilla drop, so it is not a hot-path patch. Not yet verified in-game:
  confirm seasonal appearance/absence across a season boundary via `/playthrough-test-plan`.

- **README's Pickle Vat walkthrough now describes the shipped mechanic.** Step 6 said harvesting a
  Ready vat spawns "the pickled goods" as a separate item; there is no such item, the Ready vat
  *is* the food (named for its contents, e.g. "Pickled Frogs") and holds 5 servings eaten straight
  from the vat. Reworded to describe eating servings, with the jar and reusable fired vat returned
  when the vat is emptied or destroyed. Documentation only, no gameplay change.

## [1.10.17] - 2026-09-01

### Fixed

- **Hemp Butter was unreachable. Active Hemp Butter never solidified.** The 2026-09-01 fleet
  feature audit flagged the advertised 3-dose Hemp Butter as dead content. Root cause: Active Hemp
  Butter's "Wetness" stat started at 0 with a negative drain rate, and the engine only fires an
  OnZero action on a positive-to-zero crossing, so the "solidify into Hemp Butter" transform could
  never trigger, stranding the chain one step before the card that carries the three dose actions.
  Wetness now starts full at a retuned 96 DTP (one in-game day) instead of the authored 480: spoilage
  accrued while drying transfers 1:1 into the solid butter (whose spoilage window is 480), so the old
  5-day dry time would have delivered an already-spoiled block even with the stat fixed. Melt fat
  with Hemp Flower Powder, heat to activate, let it rest a day, and the dosed butter now actually
  arrives. Not yet verified in-game.

## [1.10.16] - 2026-08-25

### Fixed

- **Reduced forage-table bloat that a player traced to a performance loss comparable to the village mod.** `AddMushroomDropsToForaging`'s biome matcher had a real double-counting bug: `isClearing` (bare `Contains("Clearing")`) also fired for `ClearingOak`/`ClearingAlder`/`ClearingPine` locations, since those names structurally contain `"Clearing"`, so those three location types received both their specific-biome drop set AND the full generic-clearing drop set (Wild Flowers, Dandelion, Common Plantain, Chamomile, Puffball, Redcurrant, Yarrow, Peanut Pod, all at high chance) stacked on top. Fixed by excluding the three specific-named clearings from the generic bucket.

### Added

- **New `ForageDropDensityScale` config (`[Performance]`, default `1.0`).** Multiplies every H&F-injected forage/dig drop chance. Lower it instead of uninstalling the mod if forage variety still feels too dense after the fix above.

## [1.10.15] - 2026-08-24

### Removed
- **Forest Scout perk removed.** The Overgrown Forest Trail gate it described has applied to every
  character by default since v1.10.7, the perk itself no longer controlled access to anything, and
  the only thing left to justify its 1-Star cost was a `+1 Foraging Aid` passive bonus (added in
  v1.10.9 as a band-aid once the gate went default). Removing the perk removes that bonus too rather
  than reinventing a hidden always-on replacement for it, the mod now has one fewer redundant
  character-creation choice. Character-creation perk count: 16 → 15.

## [1.10.14] - 2026-08-19

### Fixed

- **Pickle vat: "Uncap" no longer destroys packed food with a single accidental click.** The
  Closed-stage vat's "Uncap" button sat right next to "Seal" and discarded the 5 packed
  ingredients with no warning beyond its own tooltip text, a player checking on progress or
  misclicking lost everything and the vat "vanished" back to empty with nothing to show for it.
  Uncap now shows a confirmation prompt ("Discard the packed [food] and reopen the vat?") on
  all four variants (Frogs/Meat/Mushrooms/Vegetables) before it fires.
- **Sealed (fermenting) pickle vat now has a `DroppedOnDestroy` fallback**, matching the Ready
  stage: if it's ever removed mid-ferment, it returns the empty vat + jar instead of vanishing
  with nothing recovered.
- Fixed "The packed meat are lost" grammar in the Uncap description (→ "is lost").
- Removed 16 orphaned localization rows (`Herbs_And_Fungi_PickleVatSealed_*` /
  `PickleVatClosed_*` / `PickleVatReady_*` without a food-type suffix) left over from an earlier
  single-recipe version of the vat, unreferenced by any shipped card, but described a different,
  now-inaccurate design ("Harvest" destroying the vat outright) that could mislead anyone reading
  the CSV directly.

## [1.10.12] - 2026-08-15

### Fixed

- **Entering the foraging forest through the Portal Hub no longer strands you behind the
  Overgrown Forest Trail.** The trail's challenge card only ever seeded on the Primeval Woods
  side, so a player who teleported straight to the Foraging Path found the East exit sealed with
  no clearable path anywhere. The trail card now also seeds on the Foraging Path side, and (with
  framework 2.23.2) hacking it clear from EITHER side opens travel in both directions at once, 
  only one clearance is needed. The cleared trail still regrows after about 10 days, closing the
  route until it's hacked clear again. Requires CSFF Mod Framework 2.23.2+.
- Fixed a framework-side stripped-DA cache collision between the trail gate's two directional
  sides (both previously watched the same env) that could leave the Foraging Path's East travel
  action unrestorable after clearing, and graft a stray travel action onto Primeval Woods.
- Trail card text no longer assumes you're approaching from Primeval Woods ("open the way
  west" → "reopen the way through"); English and Chinese rows updated together.

## [1.10.11] - 2026-08-14

### Fixed

- **Removed 26 `AccessTools.Field: Could not find field … CardName` HarmonyX warnings per load.**
  `GameLoadPatch.CachedField` probed fields via `AccessTools.Field`, which logs a warning for
  every type that legitimately lacks the field (the probe runs against every loaded data type, 
  SpiceTag, QuestLog, NPCDuty, …). Switched to native `Type.GetField`, which returns null
  silently; all probed fields are public game fields, so lookup results are unchanged.

## [1.10.10] - 2026-08-13

### Fixed
- **Misty Falls (the Greenfalls copy at the west end of the foraging forest) showed two waterfall
  cards instead of one.** The cloned board seeded one from the vanilla environment template's
  default drops, then the cloned location card's inherited "Create a Waterfall if it is missing"
  maintenance action re-spawned a second one on top of it, the same duplicate-respawn failure mode
  already fixed for ACT's mining caves. `WorldMap/MapNodes.json` now declares `StripLegacyBoardUIDs`
  for Misty Falls so that maintenance action is stripped from the clone. The single remaining
  waterfall card is the hidden-path variant, which still functions as the secret route back to
  Waterfall Caves, that connection is untouched.

## [1.10.9] - 2026-08-09

### Fixed
- **Forest Scout perk (1 Star) had zero mechanical effect since v1.10.7.** The v1.10.7 fix (below)
  correctly made the Overgrown Forest Trail gate apply to every character, not just Forest Scout
  takers, but that left the perk itself granting nothing (no starting items, no stat modifiers),
  so taking it vs. skipping it was behaviorally identical while still costing a Star. Added a
  passive `+1 ForagingAid` bonus to the perk (`PassiveStatModifiers`, RM +1.0, in line with vanilla's
  constant-aid convention) so the Star cost buys something. The trail/gate mechanic itself is
  unchanged from v1.10.7.

## [1.10.8] - 2026-08-09

### Fixed
- **"Mix into Hot Water" (Dandelion, Chamomile, Yarrow, Ginseng, Reishi, Lion's Mane) failed silently
  when the water wasn't hot enough.** The action requires the water to be at least 50% temperature,
  but that requirement had no fail message, clicking the action with lukewarm/cold water did nothing
  visible, which read as "the tea didn't appear" (player-reported). All six now show "Not hot enough."
  when the heat requirement isn't met. Root cause of the underlying "which direction do I drag" confusion:
  this requirement only evaluates correctly when the water/bowl is dragged onto the herb, not the reverse
  (the herb has no temperature stat of its own), that direction still silently fails since it isn't a
  bug, just makes the message correct either way.

## [1.10.7] - 2026-08-09

### Fixed
- **The Overgrown Forest Trail now blocks the route to the foraging forest by default, not only
  after equipping the Forest Scout perk.** The perk was never actually required to hack through the
  trail (that's gated on tool tags, blade/axe/shovel/antler, on the trail's own
  `CardInteraction`); it only controlled whether the overgrowth existed at all, which meant the
  route was silently wide open on any save, including existing saves that install this mod
  without ever taking Forest Scout. Requires `CSFFModFramework` 2.21.1+ (new `SealTrigger` type
  `"Always"`).

## [1.10.6] - 2026-08-10

### Fixed
- **Bowl duplication when drinking Dandelion Tea, Chamomile Tea, or applying Herbal Salve**, reported
  by a player (2 bowls became 4 after brewing and drinking two cups). Dandelion/Chamomile Tea are
  brewed via a "Mix into Hot Water" interaction that transforms the liquid *inside* the bowl without
  ever consuming the physical ClayBowl; Herbal Salve's blueprint never required a bowl ingredient at
  all (just ground yarrow + hemp seed oil). All three DismantleActions (Drink/Apply Salve) then
  additionally spawned a brand-new ClayBowl on top of the one that was never consumed. Removed the
  redundant `ProducedCards` ClayBowl entry from all three, the original bowl already survives on its
  own once its contents are consumed. (The other 11 teas/tinctures in `CardData/Liquid/` were audited
  and confirmed correct, either their blueprint consumes a ClayBowl as an ingredient, netting to zero
  when one is returned on drink, or they use the vanilla-style liquid-card-transforms-into-bowl pattern.)

## [1.10.5] - 2026-08-07

### Fixed
- **Repaired drifted Chinese localization (`Localization/SimpCn.csv`)**, second confirmed drift
  incident (see `Documentation/Plans/Fleet/Chinese_Localization_Plan.md`). 88 keys with no Chinese row at
  all (Misty Falls environment, Mushroom Broth / Oil-Press Peanuts / Trail Mix blueprint chains,
  Cleared/Overgrown Forest Trail, Forest Scout perk) were missing entirely. Chinese players saw
  English `DefaultText` for all of them. A further 98 records had English or blank text sitting in
  the Chinese column (dried Golden/King Oyster, truffle family, herbal oils, Wooden Pantry, hemp
  seeds, and others), translated. All 1,175 legacy 2-column (`Key,Chinese`) rows normalized to the
  canonical 3-column `Key,English,Chinese` format, backfilling the English anchor from
  `SimpEn.csv` so future drift is machine-detectable (`Development_Tools/Check-LocalizationParity.ps1`).
  No existing Chinese translations were altered; all 1,203 original rows kept their exact order.
- **Fixed 3 independently-truncated `SimpEn.csv`/`SimpCn.csv` rows** (`herbsfungiwildflowers_
  UsageDurability_OnFull`, `herbsfungidandeliondried_Eat_Desc`, `herbsfungicommonplantain_
  UsageDurability_OnFull`), an unquoted comma in the English text had silently truncated it in both
  files (different truncation point in each), so English players were also seeing a cut-off string.
  Restored the full text from each item's JSON `DefaultText` and quoted the field.

## [1.10.4] - 2026-08-07

### Added
- **76 perishable items and liquids now carry `tag_Preservable`** (dried/cooked mushrooms, berries,
  herbs, teas, tinctures, salves, and the four ready pickle vats), brings them in line with the 15
  items (dried mushrooms, hemp products, roasted peanuts) that already had it. This is a vanilla
  CardTag with no shipped vanilla users; third-party mods that bulk-match spoilage-rate effects onto
  it (e.g. freshness/preservation perk mods) now apply correctly to the rest of our perishable catalog
  instead of only 15 of 91 genuinely spoiling items.

## [1.10.2] - 2026-07-27

### Changed
- **All 84 tradeable items now have NPC trading values** (previously 0 = free at trading
  tables). Prices follow vanilla's scale: berries 5–6 (dried 8–9), mushrooms 6–11, herbs 4–15,
  teas 12–20 (Ginseng Tea 45), tinctures/salves 30–60, truffles 50–120 (luxury), Ginseng
  40–55, pickling gear 8–100, oil-press parts 25–80 and Oil Press Kit 400. Part of the
  fleet-wide trading reprice (CMC 1.31.0 carries the vanilla table; framework 2.18.0 applies it).

## [1.10.1] - 2026-07-21

### Fixed
- **Apothecary quest gate (Stimulant Tea / Anti-Nausea Tea)**, `ApothecaryQuestGatePatch` no longer unlocks both teas together when CMC's Apothecary herb-fetch quest (billberries/springberries/spirit mushrooms) completes; that was the wrong signal. Each tea now unlocks independently and only when the player actually obtains the corresponding H&F item (Ground Ginseng → Stimulant Tea, Dried Ginger → Anti-Nausea Tea), matching the mod's own soft-dependency design (works with or without CMC installed).

---

## [1.10.0] - 2026-07-16

### Added
- **Peanut Oil**, press 3 Raw Peanuts + a Clay Bowl on the Oil Press (new "Press Peanuts" recipe alongside the seed/truffle/herbal pressings). Edible, seasons food, joins the `tag_Oil` lamp-fuel pool.
- **Peanut Butter**, grind Roasted Peanuts with any grinding tool (mortar & pestle, or batch-process in a grinding station). A dense, very drying fat-and-protein meal that keeps for two months.
- **Forager's Trail Mix**, new Cooking-tab recipe: 2 Roasted Peanuts + 2 Dried Billberries → 2 portions of travel ration; lighter on thirst than plain roasted peanuts.
- The peanut cycle no longer dead-ends at "roasted": all three new items consume shipped peanut content.

---

## [1.9.4] - 2026-07-12
*(Covers changes since the last published release on 2026-06-23.)*

### Added
- **Forest Scout perk**: optional 1-Star trait that adds an Overgrown Forest Trail gate between Primeval Woods and the foraging forest. Clear it with a blade, axe, shovel, or antler; the portal route remains available.
- **Medicinal herbs and preparations can now be added to stew**, including chamomile, dandelion, ginger, ginseng, reishi, yarrow, and their dried/ground/cut variants.
- **Updated peanut artwork** for pod, washed pod, raw peanuts, and roasted peanuts.

### Changed
- **Forest route gating moved to framework sealable gates**, replacing the old H&F-specific forest gate patch.
- **Overgrown Forest Trail text now matches the tools it accepts** instead of claiming only blades work.
- **Drying Kit and perk documentation were realigned** with the current 16-perk character creation roster.

### Fixed
- **Localization CSVs were deduplicated and repaired**, removing a near-2x duplicate key set, recovering missing Chinese translations, and restoring truncated English text for mushroom, drying, pantry, herbal oil, hemp field, and map-location entries.
- **Mushrooms and herb items now participate correctly in cooking/stew systems** after tag and interaction repairs across fresh, cooked, dried, ground, and sliced variants.
- **Pickle vat ready-state cleanup** removed the obsolete `PickleVatReady` location card and retired the old truffle fat-cook patch in favor of current data-driven behavior.

### Technical
- C# patching was reduced by removing obsolete `HFForestGatePatch` and `TruffleFatCookPatch` code paths.

---

## [1.8.0] - 2026-06-21

### Added
- **World map expansion**: Four new biomes are now accessible west of Primeval Woods
  - **Foraging Path**, hub node; travel west from Primeval Woods to reach it
  - **Pine Clearing**, north of Foraging Path (pine forest terrain)
  - **Oak Clearing**, west of Foraging Path (oak forest terrain)
  - **Alder Woods**, south of Foraging Path (alder forest terrain)

### Changed
- **Perk costs rebalanced**, most crafting/gathering perks now cost Suns instead of Moons:
  - Add Fungi: 2 Moons → 5 Suns
  - Alchemist: 4 Moons → 10 Suns
  - Culinary Kit: 1 Moon → 10 Suns
  - Drying Kit: 5 Suns → 15 Suns *(more expensive)*
  - Edibles Kit: 3 Moons → 10 Suns
  - Fungal Cultivator: 3 Moons → 15 Suns
  - Master Herbalist: 4 Moons → 10 Suns
  - Medical Mushrooms: 1 Moon → 15 Suns
  - Seed Bag: 5 Suns → 15 Suns *(more expensive)*
  - Smoke Kit: 4 Moons → 1 Moon *(cheaper)*
  - Mushroom Basket: 3 Moons → 1 Moon *(cheaper)*
  - Apothecary: 3 Moons → 1 Moon *(cheaper)*
  - Hemp Farmer: 4 Moons → 2 Moons *(cheaper)*
  - Add Hemp: 3 → 4 Moons *(slightly more expensive)*
- **Pickling**: harvest now yields an **Open Pickle Jar** alongside pickled goods; use the "Return Bowl" action to reclaim the clay bowl lid and reduce per-batch clay cost
- **Pickle Vat consolidation**, generic "Closed Pickle Vat" removed; closing the vat now requires choosing a type (Frogs, Meat, Mushrooms, or Vegetables) up front
- Chinese localization expanded for Hemp Field, Chanterelle, Black Trumpet, Redcurrant, Lingonberry, and all four new map locations

### Fixed
- Food tags corrected on Black Trumpet, Chanterelle, Puffball, Reishi, and Shiitake (and their cooked variants), mushrooms now properly register in stew and cooking systems

### Technical
- All mod UniqueIDs migrated from `herbs_fungi_*` underscore format to `herbsfungi*` camelCase. **⚠ Save compatibility**: items from runs using v1.7.0 or earlier will not be recognized after updating, start a new character for full compatibility.
- Pickle vat action routing migrated to CSFFModFramework Tier 2 ActionRouter.
- EA 0.65 compatibility; C# patches migrated to Tier 2 runtime APIs (ActionRouter, SpawnService).

---

## [1.7.0]

### Added
- Four foraged berries: **Blackcurrant**, **Redcurrant**, **Lingonberry**, and **Cloudberry**
  - All dryable (passive Dryness stat → dried variant), fermentable (pickle vat), and stackable up to 20
  - Each has Eat and Add to Stew actions; Cloudberry is the rarest and most nutritious

### Fixed
- Invalid JSON in berry CardHelpSection entries (literal newlines → `\n` escapes)

---

## [1.6.10]

- Version bump for release alongside framework 2.0.8 and all in-house mods

---

## [1.6.9]

### Fixed
- EA 0.63f compatibility; blueprint tab injector updated to live UI tabs (fixes journal tab on EA 0.63f)

---

## [1.6.8]

### Fixed
- Minor stability fixes; forage drop injection guard updated

---

## [1.6.7]

- EA 0.63 compatibility pass; no content changes

---

## [1.6.6]

### Added
- Mushroom log cultivation for six wood-growing mushroom types (Shiitake, Lion's Mane, Reishi, Chicken of the Woods, Golden Oyster, King Oyster); logs craft from a vanilla log + spoon auger + 5 mushrooms + wood shavings; ready after ~5 days

---

## [1.6.4]

- Compatibility pass for EA 0.62d (clean rebuild, no source changes)

---

## [1.6.1]

### Added
- Pickle vat fermentation chain (4-variant clay vessel: unfired → fired → closed → sealed → ready)
- Oil press multi-stage build chain with seed, truffle, and herbal oil recipes
- Wooden Pantry furniture storage
- 15 character-creation perks in the Situational tab
- Wild Ginger added to spice/herb roster

### Fixed
- Truffle fat-cook patch. Dried Truffle Slices + fat in heat produces Cooked Truffle instead of ash

---

## [1.4.0]

### Added
- Drying/preservation system (Drying Tray, Drying Stack)
- Medicinal teas: Ginseng, Reishi, Yarrow, Lion's Mane
- Hemp seed/flower/fiber cycle
- 11 mushroom varieties + 2 herbs
- CSFFModFramework integration
- Full localization coverage (~600 entries)

### Removed
- Hemp Addiction challenge perk and HempSatiation stat
