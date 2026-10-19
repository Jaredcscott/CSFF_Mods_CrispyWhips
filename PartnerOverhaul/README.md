# Partner Overhaul

Fixes a batch of bugs in the base game's Partner companions and adds opt-in diagnostics for reporting new ones. It also gives your Partner a voice: a short spoken conversation you can open from its Talk menu. Its only cards are the opt-in Ember Pot (off by default).

**Version:** 1.1.5
**Author:** Jared
**Language:** English (the conversation's lines are English only; all other in-game text is the game's)
**Requires:** BepInEx 5. CSFFModFramework is a soft dependency: it loads the Talk conversation and sets load order. Without it every fix still works and the Talk menu is exactly the game's.

## Installation

1. Install BepInEx 5 for Card Survival: Fantasy Forest.
2. Extract the `Partner_Overhaul` folder into `BepInEx/plugins/`.
3. Launch the game. Settings are written to `BepInEx/config/crispywhips.partner_overhaul.cfg` on first launch.

## Fixes

All of these are always on, except the powder pour fix, which has a setting.

- **Partners eat Night Broth and Stew when starving.** A hungry Partner used to drink plain water instead (the "drinks all the water when hungry" report). Thirst still targets plain water only.
  - Not yet confirmed in a recorded test run.
  - A hungry (not starving) Partner still ignores broth in its own pack. The starving duty takes a bowl on the ground that nobody owns.
  - Stew (the old stew-water card) restores nothing, even for you.
  - 1.1.5: the broth and stew are added to the Partner's Eat and Eat Free item pools directly. Before, they were given the game's Human Food tag, which other things also read (a Partner's stress eating, its food preferences, wildlife raids on stored food, QuickTransfer's tag filter and pantry slots), so they no longer carry it.
- **Partners clean finished homes again.** They cleaned a Cabin, Mud Hut or Enclosure while it was being built, then never again once it was finished.
- **Partners stop over-watering a full garden plot.** They kept draining water containers after the plot was full. A Garden Plot at 90% Hydration or more no longer offers "Water", to a Partner or to you, so no water is wasted on a full plot; below 90% it waters as before. This fix shipped in 1.1.0 but never worked until 1.1.3. Not yet confirmed in a running game.
- **Partners feed Charcoal into a Fireplace or a Fire Pit.** Fire Keeping now counts Charcoal as fuel, so a fire with only Charcoal nearby is tended. Confirmed in a scripted run on both fires. Pine Needles are not fed: Fire Keeping has no step for them, and earlier versions of this page were wrong to say otherwise.
- **Four small gaps closed.** Confirmed in a scripted run.
  - You can mark a Boar Piglet carcass for Butchering. Skinning stays listed so old marks are not lost.
  - A Partner smiths the Arrowhead and Chisel blade blanks, eats Wheat Bloodbread, and cleans the Mud Hut's Right Room.
- **Duty marks survive seasonal swaps.** Marks on a River (Dig Mud, Spearfish), a frozen River (Create Ice Hole) and a Pond (Dig Mud) no longer clear when the card freezes or thaws. A Meadowgrass patch keeps its harvest mark when it turns to Heather. The reverse cannot work, because the game replaces the Heather patch with a new one. The twin's mark menu may show a toggle that does nothing there, such as Spearfish on ice. Confirmed in a scripted run.
- **A Partner's Energy and Comfort show on its card.** The game has these status lines but hid them. Comfort covers 67 to 100, 33 to 66, -49 to -1, -75 to -50 and -100 to -76, so the game leaves 0 to 32 blank; the mod adds "Looks at ease." there (information only, no effect on the Partner). Wellbeing, Appetite, Filth and Intestines stay hidden.
- **A powder poured into a nearly full container is no longer destroyed.** The game destroys the whole card and adds only what fits. A Pouch holds 150 and one Acorn Flour pours 112.5, so a second flour vanished with 75 lost. Now a pour that cannot fit is refused and the card stays in your inventory. Setting: `Refuse Partial Powder Pours`, default ON. Confirmed in a scripted run.
- **A stray "Defecate" button is gone** from the Tree Top climb-a-tree event.
- **NPC action sounds are no longer heard from another environment.**
- **A Partner starting dialog no longer interrupts you** mid-action, such as a spirit-summon ritual.
- **Your Partner is called by name in dialog.** The Talk window title and each spoken line show the name you chose instead of "Partner". Traders, the Hag and other characters keep their own titles. If the name is empty the game's text is kept.

## Checked and not bugs

These were reported as Partner bugs and checked in a running game. Each works as the game intends.

- **Clothes "don't lighten the load or keep the Partner warm."** A worn coat does both: a Fur Coat's weight drops from 1200 to 200 in the cold. A Partner undresses when warm, and a carried coat weighs its full amount.
- **Brain-tanning "stuck on the first hide."** A Small hide takes one Brain Mush. A Large hide takes four, so the Partner stays on it longer.
- **Cauldron ownership "resetting."** A Clay Cauldron given to a Partner survives reopening its popup and a save and reload.
- **A fishing trip that can't fish.** With a Fishing Line and bait a Partner fishes for hours, about two Minnows an hour. Without bait, or with only a Fishing Rod, the trip ends at once. Press Continue on the report to send it back to its duties.
- **"Won't leave the house for water."** A Partner that is not following you sets off at Hydration 90 ("Looks very thirsty.") and drinks at the river. A following Partner waits until Hydration reaches 0, about 45 hours from 90, with rising stress and falling Favour but no lasting harm. Tell it to stop following if you want it to fetch water sooner.
- **A following Partner in the cold.** With no fire on your board it cannot warm itself. On a measured cold night its Body Temperature fell from 46 to -95 in 12 hours and it died of hypothermia about 18.5 hours in. A lit Campfire on the same board warmed it at once. Keep a fire on your board in cold weather, or tell the Partner to stop following. The opt-in **Ember Pot** below gives it a carried, weaker source of warmth.

## Opt-in options (default OFF)

These are design changes, not bug fixes.

- **Partners Tend The Hearth** lets you mark a Hearth or a Hearth (Awakened) for Fire Keeping, so a Partner feeds it Firewood, fuel and Charcoal like a Campfire, and lets a Partner warm itself at it. The mark survives the fire dying. A Hearth takes a feed only while its fuel is below 50% (a placeholder limit that may be tuned), so the big loads stay yours; this limit applies to you as well as the Partner, and Campfires and Fireplaces are unchanged. The game gives the Hearth no Partner duties, and it is already its strongest fire, so this is a choice rather than a fix. It was always on up to 1.1.2. With it off, a Hearth mark in your save does nothing and is not lost. Takes effect at game load. Checked in a scripted run, not yet confirmed.
- **Reduce Partner Move Costs** lets your Partner skip the cost of a hop between places on a chance of `Partner Move Cost Reduction (%)` (0 to 100, default 50): at 50 it skips the cost on about half of hops and pays in full on the rest. It is not a path bonus, since NPC movement has none. It applies only to your Partner: wild animals pay in full. Confirmed in a scripted run.
- **Reserved Fuel/Wood UIDs** is a comma-separated list of card UniqueIDs your Partner never picks for Fire Keeping. Charcoal can be reserved too: with its UID (`51c751469bd15ce46804e399479d1764`) listed, Fire Keeping never feeds Charcoal. Edit the config value and restart; there is no in-game screen.
  - It applies only to your Partner's Fire Keeping, on any fire that offers it, modded stoves and forges included.
  - A listed Firewood, Wood or Dried Peat makes the Partner move on to other fuel such as Twigs. If every fuel in reach is listed, it feeds nothing. Confirmed in a scripted run.
- **Partners Drink From Lakes And Streams** lets a Partner drink from a Lake, Bog, Marsh, Mire, Swamp or Stream as it does from a River. Ponds are excluded because their water is a poorer drink with a heavier infection risk. It is opt-in because the Partner walks to the closest water, so its destination can change. Takes effect at game load. Only the wiring has been checked; a Partner drinking at a lake has not been seen in a running game.
- **Cold Partner Asks For Warmth** makes a following Partner on your board ask for a fire when it "Looks very cold." (Body Temperature 1 to 50) and no fire is in reach. The game only lets it ask at "Looks hypothermic.", one step from death.
  - It uses the game's own request and cooldown. Nobody moves, and the Partner still cannot warm itself without a fire on your board.
  - Takes effect at game load. Checked in a scripted run, not yet in a hand-played game.
- **Ember Pot** adds a craftable clay pot of embers that a following Partner wears for warmth on a cold night with no fire.
  - Recipe: one Clay Bowl and one Rope, in Survival > Fire (researchable once you have a Clay Bowl).
  - Drag the empty pot onto a burning fire (any card the game tags as a fire, such as a lit Campfire) with at least an eighth of its fuel left to fill it with embers; the fire loses 12 fuel (3 hours). The embers last 6 hours and go out on their own; drag a Charcoal onto the lit pot to add 4 hours (up to 8). "Smother" puts them out.
  - Put the lit pot in your Partner's inventory and it wears it at its belt by itself, the way it puts on a coat. While worn it raises the wearer's perceived temperature by 8, less than a Campfire (10, or 15 indoors), so it slows the cold rather than replacing a fire. It also warms you if you wear it yourself. It takes the belt slot.
  - All numbers are first guesses and may be tuned. Needs CSFFModFramework. With it off the blueprint is hidden (unless your save already researched it) and a pot already in your save gives no warmth. Takes effect at game load. Not yet confirmed in a running game: whether a Partner on a cold night survives with it is the thing still to see.
- **Partner Starts Conversations** lets your Partner start the "Talk with" conversation by itself, at most once a day, only while it is on your board, and more often in the morning, and a little more often after a fight won, an escape, a finished building or the day's first fish.
  - It stays quiet while the Partner is hungry, cold, guarded, neglected, stressed or close to leaving, so the game's own requests always come first, and it never interrupts a ritual.
  - Each line takes 15 minutes and gives no stats, as when you start the talk. Needs CSFFModFramework. Takes effect at game load. Not yet confirmed in a running game.
  - The same setting gives the game's own Partner requests a voice. When your Partner comes to you hungry, cold, in need of attention or trust, or to show affection, attraction or gratitude, the game's narration ("Your partner approaches you and expresses the urgent need for food.") plays as before, and after Continue the Partner says it in its own words before the dialog goes on where the game's Continue went. The wording follows its personality (reserved, anxious, warm, assertive or plain, the same reading as the "Talk with" openers) and its mood: a stressed Partner voices a need more sharply, a happy one a warm remark more brightly. The game's effects of the request apply once, unchanged, and the spoken line itself costs no time and gives no stats. Express Blame and the Partner's ultimatum stay exactly as the game wrote them. With the setting off every request is the game's own. Wording is a first draft, English only. Not yet confirmed in a running game.
- **Expedition Stories** lets your Partner say what it brought back. After a foraging, hunting or fishing expedition, pressing Continue on the report ("Your partner has returned from a foraging expedition.") plays one short line instead of ending: mushrooms, berries, nuts and cones or herbs and roots for foraging, a big carcass or a smaller one for hunting, fish for fishing, and a plain line when it carries none of those. The line is picked from what is in the Partner's own inventory when the report opens.
  - The game's report and its effects are untouched. Each line takes 15 minutes, puts off the Partner's own next remark and gives no stats. Needs CSFFModFramework. Setting: `Expedition Stories`, default OFF because it changes a dialog the Partner starts. Takes effect at game load. Wording is a first draft, English only. Not yet confirmed in a running game; the carcass and fish matches in particular have only been read from the data.
- **Consolidate Duplicate Wounds** makes combat less dangerous. A combat round no longer adds a wound you already have, such as a 28th separate Minor Laceration, so the pain, bleeding and infection of repeat wounds no longer stack: each Minor Laceration carries its own Pain +100 and Blood Loss +0.5 and can raise your Sepsis rate. The hit's own damage, armour wear and the encounter log are untouched. Confirmed in a running game.
  - **Pick it per run with Easy Mode.** Leave the setting false and tick the Easy Mode toggle on the character screen when you start a new game: that run consolidates wounds and your other runs do not. The game's own Easy Mode adds nothing else today, so this is the only thing the toggle changes. A save you started with Easy Mode ticked gets it from 1.1.4 on. Not yet checked in a running game.

## Conversations (default ON)

- **Talk with your Partner.** The Partner's Talk menu gains an answer, "Talk with" followed by the name you chose, after the game's own Casual Talk and Express answers. It opens a short conversation in which your Partner speaks in its own words, then "Step back" closes it.
  - Each line your Partner says takes 15 minutes and, like Casual Talk, puts off its own next remark to you. It gives no stats, so it never beats Casual Talk.
  - Needs CSFFModFramework, which loads the conversation. Without it the answer is not added and the Talk menu is the game's own.
  - Setting: `Talk With Your Partner`, default ON because you choose to start it. Not yet confirmed in a running game.
  - **Your Partner opens with how it is doing.** The first thing it says depends on its state, most pressing first: cold, hungry, worn out, stressed, feeling neglected, low in spirits, happy, or a plain "I'm doing fine" when none applies. The same remarks come from a "How are you holding up?" answer on the conversation's later lines. The wording is a first draft and may be retuned. Not yet confirmed in a running game.
  - **Its opening words match its personality.** Each of those openers has four more wordings: assertive, anxious, warm and reserved. Which one plays follows the personality perks your Partner was given (Confident, Trusting, Anxious, Impulsive, Introvert, Reserved, Independent), on a new run and an old save alike; a Partner with several hears the more guarded wording. A Partner with none of those perks, such as a custom one, is read from how assertive, argumentative, trusting or solitary it is, and one who fits none hears the plain lines. Not yet confirmed in a running game.
  - **Talk about the world around you.** "Talk about..." lists what your Partner has something to say about now: the season, morning or night, the home you are in (Cabin, Mud Hut or Enclosure), a Garden Plot or a lit Hearth in the room, falling snow, rain, and a storm. With Community Mod Chest installed it also offers that mod's Village and Village Inn while you are there, and with Water-Driven Infrastructure installed it offers the grinding mill or the water mill (its Water-Driven Sawmill) while one stands on your board; without those mods those topics never appear. A topic you have heard stays out for about a season (30 days). Each topic is one line, 15 minutes and no stats. It adds fifteen hidden markers to the Partner. Not yet confirmed in a running game.
  - **Your Partner remembers what just happened.** A fight won or escaped with your Partner on your board, a structure finished there (Cabin, Mud Hut, Enclosure and other placed buildings) or the first fish of the day there gives it something to say: the next conversation can open on it, and "Talk about..." lists it. A memory lasts one day and is used once; cold, hunger and exhaustion still come first. It adds four hidden markers to the Partner. Not yet confirmed in a running game.
  - **Your Partner notices how you are.** A badly hurt, bleeding, septic, very hungry or very cold player can be the thing the next conversation opens on ("You're limping and trying not to show it."). Words only: nothing is healed or fed. It reads your state, not the Partner's. The Partner's own cold, hunger, exhaustion and memories still come first. Thresholds are first guesses. Not yet confirmed in a running game.
  - **Your Partner marks the big moments in your bond.** Once each, the first conversation after Trust or Affection reaches its top band, or after 7, 30 and 100 days together, opens on a line about it. It comes before cold, hunger and everything else, gives words only (no stat reward) and does not repeat. A Partner already past several anniversaries hears only the biggest. It adds five hidden markers to the Partner. Not yet confirmed in a running game.
  - **Your Partner can hand you a small keepsake.** At most once a season, when it looks very grateful (Favour 2401 or more) and fond of you (Affection 12001 or more), the next conversation can open on a remark about something it found while you were busy and give you one Fresh Heather Flowers at the quality the game gives that flower. It comes after the Partner's needs, memories and moods that call for care, so a Partner in need says that first. Nothing else is ever handed over, and a second talk in the same season is an ordinary one. Not yet confirmed in a running game.
  - **Your Partner remarks on new places.** The first time your Partner is with you at the lake, in the mossy and muddy oak woods, by the river, among the pine groves or under the oak groves, the next conversation can open on a line about it, once for each place. A trip it makes alone does not count, and caves, mines and homes never do. It waits behind its needs, memories, remarks on how you are and feeling neglected. Words only, 15 minutes, no stats. An old save starts with no place visited. It adds five hidden markers to the Partner. Not yet confirmed in a running game.

## Configuration

File: `BepInEx/config/crispywhips.partner_overhaul.cfg`

The optional [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) (BepInEx 5 build) changes these in game: press F1 and find "PartnerOverhaul". A setting marked "read at game load" or "read at game start" needs a relaunch after a change; the rest apply at once.

| Section | Setting | Default | Effect |
|---------|---------|---------|--------|
| Fixes | Refuse Partial Powder Pours | true | Refuse a powder pour that cannot fit whole instead of destroying the card |
| Conversations | Talk With Your Partner | true | "Talk with" answer in the Partner's Talk menu (needs CSFFModFramework; read at game load) |
| QoL | Partners Tend The Hearth | false | Hearth and Hearth (Awakened) take Fire Keeping and Warm Up, and accept a feed only below 50% fuel (read at game load) |
| QoL | Reduce Partner Move Costs | false | Your Partner sometimes skips the cost of a hop |
| QoL | Partner Move Cost Reduction (%) | 50 | Chance of skipping, 0 to 100 |
| QoL | Reserved Fuel/Wood UIDs | empty | Card UniqueIDs Fire Keeping never selects, Charcoal included (read at game start) |
| QoL | Consolidate Duplicate Wounds | false | Easier combat: repeat wounds of one type no longer stack (also on for any run started with Easy Mode ticked) |
| QoL | Partners Drink From Lakes And Streams | false | Lake, Bog, Marsh, Mire, Swamp and Stream count as drinking water (read at game load) |
| QoL | Cold Partner Asks For Warmth | false | Ask for a fire at "Looks very cold." (read at game load) |
| QoL | Ember Pot | false | Craftable Ember Pot a Partner wears for warmth (needs CSFFModFramework; read at game load) |
| QoL | Partner Starts Conversations | false | The Partner starts the "Talk with" conversation itself, at most once a day, and says its own requests in its own words after the game's narration (needs CSFFModFramework; read at game load) |
| QoL | Expedition Stories | false | The Partner says what it brought back after an expedition report (needs CSFFModFramework; read at game load) |
| Diagnostics | Enable Diagnostics | false | `[PartnerDiagnostics]` log lines |
| Diagnostics | Enable Pouch Transfer Diagnostics | false | `[PouchTransferDiagnostics]` log lines |

BepInEx keeps a value already written in an existing `.cfg`, so a changed default only reaches fresh installs.

## Reporting a Partner bug

Turn on the diagnostics you need in the config, play until the problem shows, and share `LogOutput.log`.

- **Enable Diagnostics** logs each Partner's current duty (every 10 seconds), what it wears after re-dressing, a cauldron's owners when you open it, and whether the Partner can do each item duty when that answer changes.
- **Enable Pouch Transfer Diagnostics** logs any liquid pour involving a Pouch or Acorn Flour.

## Known interactions

- **Invincibility** (a dev-only mod): while its toggle is ON, combat generates no wound, so Consolidate Duplicate Wounds has nothing to do.
- **Advanced Copper Tools** also hooks the wound step for its armour fix. The two do not interact.

## Version history

Full notes are in `CHANGELOG.md`.

- **1.1.5:** "Talk about..." can be reached from "Talk with" on default settings. New conversation lines: first visits to new places, a seasonal keepsake and a water mill topic. Opt-in Ember Pot and Expedition Stories, and with Partner Starts Conversations on, your Partner voices its own requests. Broth and stew join the Partner's Eat pools instead of carrying the Human Food tag, and a Partner-started talk counts toward its once-a-day limit.
- **1.1.4:** Your Partner is called by name in the Talk window and its dialog log, and a "Talk with" answer opens a short spoken conversation: openers by mood and personality, topics, memories, remarks on how you are, and bond milestones. Also the opt-in Partner Starts Conversations, Charcoal can be reserved, a tended Hearth takes a feed only below 50% fuel, Easy Mode merges duplicate wounds for its run, and a "Looks at ease." Comfort status.
- **1.1.3:** The garden plot fix works; Hearth tending becomes an opt-in setting.
- **1.1.2:** Partners eat Night Broth and Stew when starving.
- **1.1.1:** Version number only.
- **1.1.0:** Hearth tending, seasonal duty marks, Comfort display, powder pour fix, and the lake-drinking and cold-warmth options.
