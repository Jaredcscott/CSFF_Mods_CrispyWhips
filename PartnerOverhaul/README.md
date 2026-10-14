# Partner Overhaul

Fixes a batch of bugs in the base game's Partner companions and adds opt-in diagnostics for reporting new ones. It adds no cards: it only patches vanilla content.

**Version:** 1.1.2
**Author:** Jared
**Language:** English (the mod has no text of its own; all in-game text is the game's)
**Requires:** BepInEx 5. CSFFModFramework is a soft dependency, used only for load order.

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
- **Partners clean finished homes again.** They cleaned a Cabin, Mud Hut or Enclosure while it was being built, then never again once it was finished.
- **Partners stop over-watering a full garden plot.** They kept draining water containers after the plot was full.
- **Partners feed Charcoal into a Fireplace or a Fire Pit.** Fire Keeping now counts Charcoal as fuel, so a fire with only Charcoal nearby is tended. Confirmed in a scripted run on both fires. Pine Needles are not fed: Fire Keeping has no step for them, and earlier versions of this page were wrong to say otherwise.
- **Partners tend a Hearth and warm themselves at it.** A Hearth and a Hearth (Awakened) can now be marked for Fire Keeping and are fed Firewood, fuel and Charcoal like a Campfire. The mark survives the fire dying. Checked in a scripted run, not yet confirmed.
- **Four small gaps closed.** Confirmed in a scripted run.
  - You can mark a Boar Piglet carcass for Butchering. Skinning stays listed so old marks are not lost.
  - A Partner smiths the Arrowhead and Chisel blade blanks, eats Wheat Bloodbread, and cleans the Mud Hut's Right Room.
- **Duty marks survive seasonal swaps.** Marks on a River (Dig Mud, Spearfish), a frozen River (Create Ice Hole) and a Pond (Dig Mud) no longer clear when the card freezes or thaws. A Meadowgrass patch keeps its harvest mark when it turns to Heather. The reverse cannot work, because the game replaces the Heather patch with a new one. The twin's mark menu may show a toggle that does nothing there, such as Spearfish on ice. Confirmed in a scripted run.
- **A Partner's Energy and Comfort show on its card.** The game has these status lines but hid them. Comfort covers 67 to 100, 33 to 66, -49 to -1, -75 to -50 and -100 to -76, so a Partner between 0 and 32 shows none, which is the game's own gap. Wellbeing, Appetite, Filth and Intestines stay hidden.
- **A powder poured into a nearly full container is no longer destroyed.** The game destroys the whole card and adds only what fits. A Pouch holds 150 and one Acorn Flour pours 112.5, so a second flour vanished with 75 lost. Now a pour that cannot fit is refused and the card stays in your inventory. Setting: `Refuse Partial Powder Pours`, default ON. Confirmed in a scripted run.
- **A stray "Defecate" button is gone** from the Tree Top climb-a-tree event.
- **NPC action sounds are no longer heard from another environment.**
- **A Partner starting dialog no longer interrupts you** mid-action, such as a spirit-summon ritual.

## Checked and not bugs

These were reported as Partner bugs and checked in a running game. Each works as the game intends.

- **Clothes "don't lighten the load or keep the Partner warm."** A worn coat does both: a Fur Coat's weight drops from 1200 to 200 in the cold. A Partner undresses when warm, and a carried coat weighs its full amount.
- **Brain-tanning "stuck on the first hide."** A Small hide takes one Brain Mush. A Large hide takes four, so the Partner stays on it longer.
- **Cauldron ownership "resetting."** A Clay Cauldron given to a Partner survives reopening its popup and a save and reload.
- **A fishing trip that can't fish.** With a Fishing Line and bait a Partner fishes for hours, about two Minnows an hour. Without bait, or with only a Fishing Rod, the trip ends at once. Press Continue on the report to send it back to its duties.
- **"Won't leave the house for water."** A Partner that is not following you sets off at Hydration 90 ("Looks very thirsty.") and drinks at the river. A following Partner waits until Hydration reaches 0, about 45 hours from 90, with rising stress and falling Favour but no lasting harm. Tell it to stop following if you want it to fetch water sooner.
- **A following Partner in the cold.** With no fire on your board it cannot warm itself. On a measured cold night its Body Temperature fell from 46 to -95 in 12 hours and it died of hypothermia about 18.5 hours in. A lit Campfire on the same board warmed it at once. Keep a fire on your board in cold weather, or tell the Partner to stop following.

## Opt-in options (default OFF)

These are design changes, not bug fixes.

- **Reduce Partner Move Costs** discounts the resource cost of a Partner's travel by `Partner Move Cost Reduction (%)` (0 to 100, default 50). It is a general discount, since NPC movement has no path bonus. It applies only to your Partner: wild animals pay in full. Confirmed in a scripted run.
- **Reserved Fuel/Wood UIDs** is a comma-separated list of card UniqueIDs your Partner never picks for Fire Keeping. Edit the config value and restart; there is no in-game screen.
  - It applies only to your Partner's Fire Keeping, on any fire that offers it, modded stoves and forges included.
  - A listed Firewood, Wood or Dried Peat makes the Partner move on to other fuel such as Twigs. If every fuel in reach is listed, it feeds nothing. Confirmed in a scripted run.
- **Partners Drink From Lakes And Streams** lets a Partner drink from a Lake, Bog, Marsh, Mire, Swamp or Stream as it does from a River. Ponds are excluded because their water is a poorer drink with a heavier infection risk. It is opt-in because the Partner walks to the closest water, so its destination can change. Takes effect at game load. Only the wiring has been checked; a Partner drinking at a lake has not been seen in a running game.
- **Cold Partner Asks For Warmth** makes a following Partner on your board ask for a fire when it "Looks very cold." (Body Temperature 1 to 50) and no fire is in reach. The game only lets it ask at "Looks hypothermic.", one step from death.
  - It uses the game's own request and cooldown. Nobody moves, and the Partner still cannot warm itself without a fire on your board.
  - Takes effect at game load. Checked in a scripted run, not yet in a hand-played game.
- **Consolidate Duplicate Wounds** stops a combat round from adding a wound you already have, such as a 28th separate Minor Laceration. Damage, stats, armour wear and the encounter log are untouched. Confirmed in a running game.

## Configuration

File: `BepInEx/config/crispywhips.partner_overhaul.cfg`

| Section | Setting | Default | Effect |
|---------|---------|---------|--------|
| Fixes | Refuse Partial Powder Pours | true | Refuse a powder pour that cannot fit whole instead of destroying the card |
| QoL | Reduce Partner Move Costs | false | Discount your Partner's travel resource cost |
| QoL | Partner Move Cost Reduction (%) | 50 | Size of that discount, 0 to 100 |
| QoL | Reserved Fuel/Wood UIDs | empty | Card UniqueIDs Fire Keeping never selects (read at game start) |
| QoL | Consolidate Duplicate Wounds | false | No repeated same-type wound cards in combat |
| QoL | Partners Drink From Lakes And Streams | false | Lake, Bog, Marsh, Mire, Swamp and Stream count as drinking water (read at game load) |
| QoL | Cold Partner Asks For Warmth | false | Ask for a fire at "Looks very cold." (read at game load) |
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

- **1.1.2:** Partners eat Night Broth and Stew when starving.
- **1.1.1:** Version number only.
- **1.1.0:** Hearth tending, seasonal duty marks, Comfort display, powder pour fix, and the lake-drinking and cold-warmth options.
