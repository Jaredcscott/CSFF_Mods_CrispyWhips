# Changelog

## [0.1.7] - 2026-10-18

### Added

- **A Deer Lick at the Mud Springs.** A trampled hollow of salty mud the deer come to. **Wait at the Lick** (2 hours) and a doe or a stag may come down for the salt; the base game's deer sighting then appears, and you can hunt it or let it be. It is the world's own doe or stag, so a deer you have killed does not come back to the lick until the base game brings it back. Each wait uses up some of the lick's salt: three waits from a full lick, then about one every two days. The odds (about 3 in 10 for a doe, 3 in 20 for a stag, otherwise nothing) are placeholders, not balanced. A save that has already visited the Mud Springs will not gain the lick; a new game does.

### Changed

- **No second clay pit at the Abandoned Homestead.** The Homestead is a copy of the vanilla Clay Banks, and its two **Clear** actions used to turn up Clay as well. They no longer do; the rest of what clearing gives (wood, twigs, leaves, moss, stones, bugs and now and then flint) is unchanged, and the vanilla Clay Banks next door is still the place for clay.
- **Dig Peat at the Mud Springs gives 2 Wet Peat Turf** (was 6). The Mud Springs are a copy of the vanilla Fetid Bog; the full six per dig stays at the vanilla Fetid Bog and Peat Bog. Both changes apply to existing saves as well.

### Fixed

- The Rusty Axe that the Ossuary's **Sift the Mounds** can turn up now shows a picture (the game ships it with none, so the card was blank). It is the same old axe as the Blunt Rusty Axe, with a freshly honed edge.

## [0.1.6] - 2026-10-16

### Added

- **Flavour finds at the Ossuary and the Bog Body.** **Sift the Mounds** has about a 6 percent chance to turn up a Rusty Axe, and **Search the Body** about a 6 percent chance each for a Copper Fish and a Copper Turtle (placeholder chances, not balanced). These are vanilla cards the base game never hands out. The Bones, Bone Pieces and bronze nugget finds are unchanged.
- **Weeping Pine at Barrow Wood.** A lone old pine among the barrows. **Collect Resin** (15 minutes) gives a vanilla Cold Resin, four collections from a full tree, then about one every five days while the scars refill (a vanilla Resin Tap gives one a day). A place's cards are written into a save only when its board is first created, so a save that has already run Forest Lore will not gain the pine; a new game does. It uses the vanilla pine picture until bespoke art is made.

### Changed

- **The Cave Lake says it is winter water.** Its description now says what was already true: it never freezes, so in winter, when the rivers need an ice hole, it is still open water. Text only.
- **The four reading actions give a small calm, once a day.** Read the Stones, Read the Stone, Read the Line and Study the Paintings now lower Stress by 10 (placeholder value, not yet balanced), like the base game's Gaze at Water spends time for calm. Each site card carries a new one-point stock that refills about once a day; reading spends it, and a second reading the same day is refused with a message. The reading text still shows. On an old save the stock loads full.

### Fixed

- The Blunt Rusty Axe that the Ancient Cairn and the Old Battlefield give now shows a picture (the game ships it with none, so the card was blank).

## [0.1.5] - 2026-10-15

A balance pass to bring Forest Lore in line with the base game rather than ahead of it.

### Changed

- **Fewer bronze nuggets.** Opening the Ancient Cairn and searching the Old Battlefield now find a Tin Bronze nugget one time in five (was two in five). All the sites together give about four on average, so a full Tin Bronze crucible (six) now needs some luck or a trader.
- **The Sunken Lake is a stocked eel pond.** It holds ten Eels, shown on the card; each Eel caught takes one and the lake refills about one a day. While no whole Eel is left, Fish and Spearfish are refused. A line now catches an Eel about one cast in seven at no skill (was two in five) and about one in two at full Fishing skill. On a save from 0.1.4 the lake starts full.
- **The Mud Volcano warms you by +3** Perceived Temperature (was +8), like a mud hut room. Vanilla only warms you indoors.
- **Scoop Mud gives three Mud Piles** (was one), the same as digging mud by hand at a pond.
- **The Homestead stores and lasts like vanilla shelters.** It holds 4000 weight, as much as a Storage Chest (was 10000), and decays like the vanilla Hideout: about 30 days after it was raised or last repaired it falls back into a Ruined Homestead and its contents spill onto the floor. Drag a Wooden Plank onto it (**Repair**) to restore the full 30 days. On a save from 0.1.4 an existing Homestead starts with the full 30 days, and if it holds more than 4000, nothing already stored is thrown out: you just cannot add more until it is under the limit (read from the game's code, not yet seen in a game).

### Fixed

- Fishing and spearfishing at the Cave Lake can now come up empty (85 of 100 casts, half of spear throws): the miss never happened before, so every try caught an eel or a crayfish.

## [0.1.4] - 2026-10-14

### Changed

- **The lore sites stay still again.** Their slow drift (0.1.3) is off for now. The Tree Hollow's branches still sway.

## [0.1.3] - 2026-10-14

### Added

- **The lore sites drift slowly**, from the Ancient Cairn to the Tree Hollow, whose branches sway. Needs CSFF Mod Framework 2.28.3.

## [0.1.2] - 2026-10-13

### Fixed

- **The Fairy Hole Ladder (Foot) now helps the climb up from The Shaft.** The vanilla Climb Up there ignored Climbing Aid, so the ladder changed nothing. The mod now adds Climbing Aid to that climb's success odds, and a skill-0 climb goes from about 53 to about 77 percent with the ladder built. Checked in a scripted run; a check in a normal game is still pending.

## [0.1.1] - 2026-10-12

### Added

- **Simplified Chinese translation** for all 217 text rows, so Chinese players no longer see English for this mod.

### Fixed

- **The Climbing Aids can now be built.** Carved Stairs and both Fairy Hole Ladder ends never appeared in the Improvements tab. Each now shows up the first time you stand on one of its boards. A scripted run built each one and saw the raised Climbing Aid.

### Changed

- **Sixteen pictures redrawn closer to the base game's card art:** Ancient Cairn, Battlefield, Bog Body, Cave Lake, Cave Mouth, Cave Paintings, Homestead, Ladder Foot, Ladder Top, Mud Volcano, Overhanging Rock, Petrospheres, Ruined Homestead, Standing Stones, Tar Seep and Tree Hollow. The Cave Lake tunnel now sits at the end of the stone path, and the Cave Mouth, Mud Volcano and Overhanging Rock no longer show footprints. No card data changed.

## [0.1.0]
- Scaffold: the mod loads and logs its one startup line.
- Barrow Wood: a new place south of the Old Glade (a copy of the vanilla Old Grove), joined by a compass link, so the Old Glade gains a South exit.
- Ancient Cairn, a finite site in Barrow Wood: Clear the Entrance (three times), then Open the Tomb four times for Bones, Bone Pieces, a chance of a Tin Bronze Metal Nugget (Metal Quality 40) and a rare Blunt Rusty Axe. Once emptied it stays empty. Counts and chances are tunable placeholders.
- Standing Stones, Circle Stone and Line of Petrospheres in Barrow Wood: each can be read any number of times and searched once (Flint and a Tin Bronze Metal Nugget; Small Stone and Flint; Small Stone). Counts and chances are tunable placeholders.
- Tree Hollow in Barrow Wood: Nap and Sleep as the vanilla Hideout's, with +1 Rain Protection and +1 Sun Protection while you are on its board; it never decays and cannot be repaired or deconstructed.
- Old Battlefield: a new place north of the Moon Hills (a copy of the vanilla Badger Hill), joined by a compass link, so the Moon Hills gain a North exit. Its field can be searched six times for Bones, Bone Pieces, a chance of a Tin Bronze Metal Nugget (Metal Quality 40) and a rare Blunt Rusty Axe; its Fallen Watchtower can be cleared once for Small Stones, Wood and Long Sticks. Both stay spent once emptied. Counts and chances are tunable placeholders.
- Abandoned Homestead: a new place east of the Clay Banks (a copy of the vanilla Clay Banks), joined by a compass link, so the Clay Banks gain an East exit. Its Ruined Homestead is salvaged four times for Long Sticks, Twine and sometimes Small Stones and Wood, then repaired with six Planks or Ropes dragged onto it, then Raise the Roof turns it into the Homestead: Nap and Sleep as the vanilla Hideout's, +1 Rain Protection and +1 Sun Protection while you are there, six storage slots, no decay. A Grinding Slab by the door mills a millable card like a vanilla Saddle Quern that cannot be moved. Counts, repair materials and storage are tunable placeholders.
- A save that already visited Barrow Wood will not gain the four new sites (a place's cards are written into a save only when its board is first created); a new game does.
- Mud Springs: a new place south of the Peat Bog (a copy of the vanilla Fetid Bog), joined by a compass link, so the Peat Bog gains a South exit. Three patches that refill over time: the Mud Volcano (Scoop Mud gives Mud, eight scoops, and warms you +8 Perceived Temperature while you are on its board), the Salt Crust (Scrape Salt gives Salt, two scrapes, slow to regrow) and the Tar Seep (Collect Tar gives Hot Tar, four collections). No eruption, timer or cost. Yields, refill rates and the warmth are tunable placeholders.
- Rock Shelter and the Sunken Caves: a new place west of the Old Hollow (a copy of the vanilla Pine Grove), joined by a compass link, so the Old Hollow gains a West exit. Its Cave Mouth (Enter the Caves) leads down into three chambers in a line, copies of the vanilla Waterfall Caves with their own exit, shrine and flint vein removed and off the paper map: the Painted Chamber (a Cave Exit, Leave the Caves, climbs back to the Rock Shelter), the Ossuary and the Sunken Lake, joined West and East by compass buttons. The Sunken Lake's Cave Lake gives Drink, Fill and Wash yourself on the vanilla pond's actions (the water is unsafe, as a pond's is) and Fish and Spearfish on vanilla's fishing line and spear tags, for Eels and Crayfish, in every season with no ice to cut; it has no frogs, leeches, Gaze at Water or fish stock. Catch chances are tunable placeholders. The caves are lit as the Waterfall Caves template is; they are not darkened.
- Cave Contents: an Overhanging Rock on the Rock Shelter (Nap and Sleep as the vanilla Hideout's, +1 Rain Protection and +1 Sun Protection while you are on its board, never decays), Cave Paintings in the Painted Chamber (Study the Paintings, as often as you like, nothing to take) and Bone Mounds in the Ossuary (Sift the Mounds once for 2 to 3 Bones and 2 to 4 Bone Pieces, then spent for good). The bone counts are tunable placeholders. A save that already ran the mod will not gain them (a place's cards are written into a save only when its board is first created); a new game does.
- The Squeeze: the passage between the Ossuary and the Sunken Lake is shut in both directions while your carried load (the Encumbrance stat) is above 1000, and open again when it is not, with a read-only Size Up the Squeeze card on each side. The limit of 1000 is a tunable placeholder (eight small stones read 100). A save that already ran the mod will not gain the cards; a new game does.
- Guano Rock: a new place east of the Lake Island (a copy of the vanilla Willow Ways), joined by a compass link, so the Lake Island gains an East exit (a swim, like its own "Swim West": you get wet, in every season) and the rock's West exit leads back. Its Guano Ledge refills over time: Scrape Guano gives vanilla Manure, eight scrapes from a full ledge, for the Compost Bin and the fields. The yield, refill rate and scrape count are tunable placeholders. A save that already ran the mod will not gain the ledge; a new game does.
- Bog Body: digging peat at the vanilla Peat Bog (Dig Peat) now has a small chance (about one dig in 33, a tunable placeholder) to turn up a Bog Body, once per game. Search the Body works once for a Tin Bronze Metal Nugget (Metal Quality 40) and 2 to 3 Bones (placeholders). Dig Peat still gives its six Peat every time. This is the mod's one edit to a vanilla card: an append-only `GameSourceModify` patch adds one drop to the Peat Bog's Dig Peat interaction, and nothing else on the card changes. Unlike the places, a save that already ran the mod can find it.
- Known limitation: an item left on a chamber's board (other than the lake's catches) is lost, and the chamber's sites are refilled, the next time a save taken elsewhere is loaded, until the framework's run-start tidy is fixed (CSFFModFramework plan section 30). Carry things out of the caves; in the Ossuary a bone left there also refills the Bone Mounds.
- Climbing Aids: Carved Stairs (a mining tool and a shovel held, 8 Stone, 10 points) can be built on the eleven vanilla boards with a climb of their own (Damp Hollow, Dead Hollow, Secret Grove, Crow Pines, Mountain Grove, Wind Needles, Greenfalls, Southern Rapids, Shady Thicket, Cliff Thicket, Pine Cliffs), and a Fairy Hole Ladder at each end of the Fairy Hole (Top on Rocky Grove, Foot on The Shaft; 4 Planks and 2 Ropes, 6 points each). Each raises the vanilla Climbing Aid stat while you stand on that board (+40 for the stairs, +120 for a ladder end; tunable placeholders), so a climb there fails less often and skill still counts. No vanilla card is edited: the improvements are injected into the boards' improvement lists at load. Vanilla's Climbing Aid status bands show while the aid is raised, and a large tree's Climb on the same board is helped too. Not yet seen to build in a running game.
- Card art: every card has a picture. The Ancient Cairn, Standing Stones, Circle Stone, Line of Petrospheres, Tree Hollow, Fallen Watchtower, Ruined Homestead, Homestead and Grinding Slab came first; the Old Battlefield, Mud Volcano, Salt Crust, Tar Seep, Guano Ledge, Bog Body, Cave Mouth, Cave Exit, Cave Lake, Overhanging Rock, Cave Paintings, Bone Mounds, the Squeeze, Carved Stairs and the two Fairy Hole Ladder ends followed. English only.
