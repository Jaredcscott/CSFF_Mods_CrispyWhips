# Utility Belt

**Version:** 0.1.1
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.68b)
**Requires:** BepInEx 5 (Quick Transfer and Skill Speed Boost also need CSFF Mod Framework)

Utility Belt is a quality-of-life mod that gathers small utilities into one download. It is in early development: this version includes **Repeat Action**, which repeats your last action with one keypress, **Quick Transfer**, which moves several cards at once, and **Skill Speed Boost**, which tunes how fast skills train. Its own features so far are small accessibility, display and inventory options, described under Utility Belt itself below: hiding falling rain and snow, a 12-hour clock, equipped-bag hotkeys, item locks, container sorting, capacity and warmth readouts, quickswap when equipping, a sealed-container mark and a character randomiser. More are planned for later versions.

Quick Transfer, Repeat Action and Skill Speed Boost keep shipping as separate mods. Utility Belt is for players who would rather install one thing, and every included mod can be removed by deleting its DLL.

**Language:** English and Simplified Chinese (`Localization/SimpEn.csv`, `SimpCn.csv`)

## Feature overview

| Part | Features |
|------|----------|
| Repeat Action 2.2.1 (included) | Repeat last action, adjustable count, travel repeat, safety stops, spare tool, pinned action, pause/resume |
| Quick Transfer 1.9.0 (included) | Modifier + right-click bulk transfer, adjustable amounts, tag filter, undo, transfer hotkey |
| Skill Speed Boost 1.11.0 (included) | Area Familiarity (on), XP multipliers, staleness, optional bonuses, difficulty profiles |
| Utility Belt itself | Hide Rain And Snow, 12-hour clock, equipped-container hotkeys (cycle plus five direct keys), Item lock, Sort Container, capacity and warmth readouts, Quickswap, Sealed mark, Randomise button, Auto-Pull Materials, Store Products In and Equip Spare Tool On Break for Repeat Action, module-source diagnostic |

Utility Belt adds no cards, items or blueprints.

## Features

### Repeat Action (included, version 2.2.1)

- **Repeat Last Action**: press `Shift+R` to repeat your most recent action (Forage, Clear, Chop, Travel, a craft and so on). Press it again to cancel.
- **Adjustable Count**: `Shift+Plus` and `Shift+Minus` (or `Shift+Mouse Wheel`) change how many times to repeat. Hold either key for rapid adjustment. Lower the count below 1 for **Unlimited** mode, which runs until a safety stop, a failed requirement or you cancel (a backstop, 500 iterations by default, ends a run that can never fail).
- **Most kinds of action**: single actions, drag-and-drop crafts (making twine, soaking reeds, chopping with a tool), stack buttons on card piles, and group actions such as Eat All. Blueprint Builds are not repeated.
- **Travel**: repeat a direction (North, South, East, West) to keep moving. The run stops cleanly when there is no path forward.
- **Safety stops**: stops when a status condition starts blocking actions (starving, dehydrated, exhausted), at stat floors you set (Satiation, Hydration, Stamina, or any stat through `Extra Stat Thresholds`), when a drag-drop tool breaks or drops below a durability you choose, and optionally when your containers are full, at nightfall, at a season change, after a number of in-game hours, or once you hold a set quantity of a card.
- **Spare tool** (off by default): when a dragged tool breaks, carry on with another tool of the same kind from your current location instead of stopping.
- **Pinned action**: `Shift+P` pins the action `Shift+R` would replay, and `Shift+F` repeats the pinned action later, even after doing other things or travelling.
- **Pause and resume**: `Shift+G` pauses a run after its current iteration and resumes it.
- **Feedback**: on-screen progress notifications with a fill bar, an optional count indicator in the top-right corner, and an optional completion sound. Notifications are in English or Simplified Chinese to match the game language.
- **Honest stop reasons**: when a run ends early, the notification gives the game's own reason, not a guess. The mod never answers an encounter for you.

Everything above is a setting in `BepInEx/config/crispywhips.repeat_action.cfg`. The settings and the included Repeat Action are the same as the standalone Repeat Action 2.2.1, whose own page lists every option.

### Quick Transfer (included, version 1.9.0)

- **Bulk transfer**: hold a modifier and **right-click** a card in an open container or your Item row. `Shift` moves 5 cards, `Ctrl` moves 10, and `Ctrl+Shift` moves the whole stack (or half, or all but one, by setting). The direction is the game's own: out of a container into your Item row, or from your Item row into the open container.
- **Adjustable amounts**: hold `Ctrl` and press `Plus` or `Minus` to change the Ctrl amount, `Alt` and `Plus` or `Minus` to change the Shift amount, and `Backspace` to reset all of them. An on-screen label shows how many cards a click will move.
- **More ways to choose cards**: Tag Filter (hold `Caps Lock` to take every card sharing the clicked card's tag), Spoiled Only, Drain Matching Stacks, Prioritize Most Spoiled, a hover-and-press Transfer Hotkey (`X`), Undo (`Z`) and per-container settings. The on-screen text is in English or Simplified Chinese to match the game language.

Everything above is a setting in `BepInEx/config/crispywhips.quick_transfer.cfg`. The settings and the included Quick Transfer are the same as the standalone Quick Transfer 1.9.0, whose own page lists every option.

**Quick Transfer needs CSFF Mod Framework.** It uses the framework to find the game's card-click handler and to load its overlay text, so without the framework installed bulk transfer does not apply. Repeat Action and Utility Belt itself do not need it.

### Skill Speed Boost (included, version 1.11.0)

**Out of the box this changes how fast your skills level in one way: Area Familiarity is on.** Working at a place you have worked before earns up to +30% skill XP there, growing over about 80 visits to that place. Everything else keeps the game's own rates: skills go stale exactly as in vanilla, the global and per-skill XP multipliers are 1, and every other bonus (morning and night bonuses, well-rested, streaks, level scaling and the rest) is off until you turn it on. To keep XP fully vanilla, delete `Skill_Speed_Boost.dll` from `Utility_Belt/` (or set `AreaFamiliarityEnabled = false` in `BepInEx/config/crispywhips.skill_speed_boost.cfg`).

- **XP multipliers**: one global multiplier (1 to 10) and a multiplier for each skill, including skills other mods add.
- **Staleness**: keep the game's own staleness penalty, or switch it off.
- **Optional bonuses** (all off by default): morning and night windows, daily first use, well-rested, an in-the-flow bonus, a day streak, a rested bank, milestones, level scaling, skill synergies, and bonus maps by station, season, place, tool and action.
- **Difficulty profiles**: named presets (Balanced, Casual, Hardcore and others) that set the whole stack with one key.

Skill Speed Boost's settings and its familiarity records (`BepInEx/config/SkillSpeedBoost/`) are shared with the standalone mod. Its own page and `FEATURES.md` list every option; Utility Belt ships no copy of that file. The included Skill Speed Boost is the same as the standalone Skill Speed Boost 1.11.0 and needs CSFF Mod Framework for its staleness, familiarity and morning-bonus features.

### Utility Belt itself

- **Hide Rain And Snow Effects** (Accessibility, off by default): for players who find the falling weather hard to look at, for example for photosensitivity. When on, the falling rain and snow drawn over the screen while you are outside are hidden: the game's two weather-effect layers are switched off every frame, the same way the game itself switches them off when you are inside a shelter. Nothing else changes: the weather card, its sound, its colour tint of the scene and its effects on your character (wetness, cold and so on) stay as the game makes them. In Clear weather there is nothing to hide, so the option changes nothing there. With it off, Utility Belt changes nothing on screen.

- **Clock Format** (Display, `TwentyFourHour` by default): set it to `TwelveHour` to show the main clock beside the day counter in a compact 12-hour form, "9:45p" instead of "21:45" (an `a` for morning, a `p` for afternoon and evening; midnight is 12:00a, noon is 12:00p). Only that clock changes: save-slot times and the BepInEx log stay 24-hour. Simplified Chinese shows a small 上午 or 下午 before the time ("下午9:45"). The 12-hour text is longer than "21:45", so when it would be wider the clock shrinks it (a rich-text size tag) to take no more room than the 24-hour text did: in English the digits end up a little smaller, in Chinese about two thirds of their usual size. The game's own font size is not touched. With the default, Utility Belt changes nothing on screen.

- **Equipped-container hotkeys** (Hotkeys): press the backquote key (`` ` ``, left of `1`) to open your next worn bag, sack or other worn card with an inventory, in the order the equipment row shows them, wrapping back to the first after the last. It opens the bag exactly as clicking it does, so the game's own rules apply (nothing opens while an action is running), and pressing it with only one bag worn closes that bag again, as a second click would. A worn knife sheath or spear carrier has an inventory too, so it is one of the stops. Five more keys, Open Equipped Container 1 to 5, open one bag directly; they are off (`None`) by default. Avoid the number keys `1` to `9` for them: the game uses those for bookmarks whatever modifier you hold.

- **Item lock** (Hotkeys, `L` by default): open a container (a chest, a bag), rest the pointer on a card inside it and press `L`. A small padlock appears on the card, and the game's **Take out all items** button then leaves it where it is while everything else comes out. Press `L` on it again to unlock it. Exactly what a lock covers:
  - Take out all items, Quick Transfer batches and Repeat Action runs honour it: a batch never moves a locked card, and a run never picks one up as a new target or ingredient (the exact card you used for the first repeat stays eligible). Dragging the card yourself and every other way a card moves ignore it. Quick Transfer and Repeat Action look the lock up when Utility Belt is installed (their standalone copies do too); without Utility Belt they behave as before.
  - A lock belongs to one card, told apart by its kind and the moment it was made. Cards that arrived together (one harvest, one craft, one spawned stack) were made on the same tick, so they lock and unlock together. Two of the same item made at different times lock separately.
  - A card that changes into something else (cooked, dried, broken) is a new card, so the lock does not follow it.
  - In a pile only the top card's padlock can be seen, so a locked card under unlocked ones shows no padlock until it is on top.
  - Locks are saved per save slot in `BepInEx/config/UtilityBelt/ItemLocks_Slot<N>.tsv`, never inside the game's own save, and written the moment you press the key. When you load a save, locks whose cards no longer exist anywhere in it are dropped; starting a new game in a slot clears that slot's locks.
  - Recipe containers clearing for a saved recipe, and a blueprint being switched or taken down, still empty completely.

- **Sort Container** (Hotkeys, `O` by default): with a chest, bag or other container open, each press of `O` changes its order. The first press puts the freshest food first (by how full each card's spoilage bar is), the second puts the highest Quality first, and the third puts everything back in the order it had before your first press. The next press after that starts a new cycle. Exactly what it does:
  - It reorders the slots inside the one open container, and the cards inside each pile; no card leaves the container. A slot holding a locked card (see Item lock) stays where it is, and the other slots are ordered around it.
  - Cards of one kind always stay in one pile, because the game merges them every time a container opens, so two piles of the same item cannot be split apart by freshness. A pile is ranked by its best card and its own top card is the best one. Cards with no spoilage bar sort last under freshness, and cards with no Quality bar sort last under Quality, in the order they were in.
  - "Quality" is the card's own bar named Quality, as a fraction of its maximum (foods have one; some tools and parts have one). A card without one is treated as having none.
  - The order you leave a container in is kept when you save and load, and when you close and reopen it. What the third press goes back to is remembered only until you close that container's window.
  - It does nothing in the small built-in inventories (a Cooking Pot, for example), cooking containers, fixed-slot stations and containers holding a saved recipe.

- **Capacity and warmth readouts** (Display > Show Capacity And Warmth Readouts, off by default): when on, the card's Info tab adds one short line under its description. The game shows a container's fill only as a bar and a percentage, never as numbers, and shows no warmth figure for a bed or cover at all. Exactly what it shows:
  - A container with a weight capacity shows `Weight: 1250 / 4000` (what it holds against what it can hold, in the game's own weight units, including the weight of any liquid inside).
  - A container you fill by pouring (a bowl, a jar, a pot, a cauldron) also shows `Liquid: 300 / 300`. Garments, tools and arrows have a hidden liquid capacity the game uses for a coating they soak up; it is never shown, because you cannot pour into them.
  - A bed, shelter or hide you sleep on or under shows `Warmth while sleeping: +2`: the Perceived Temperature the card gives while its user sleeps, read from the card's own data at the moment you open it, so modded beds and covers show theirs too. A card with several such effects (a different one per hide grade, say) shows the largest, and the effect that applies at a given moment can be smaller when the card's other conditions are not met. Worn clothing shows no line. No comfort figure is shown: the game has no number to read for it.
  - The line is added to the Info tab only; the Contents tab shows no description to add it to. It is recomputed each time the game redraws the open card's contents. Simplified Chinese shows the same lines in Chinese. With the option off Utility Belt adds nothing to any popup.

- **Quickswap** (Equipment > Quickswap When Equipping, off by default): when on, equipping a card whose equipment slot is already full swaps it with the one you wear instead of refusing. The worn card goes back to where the new one came from (the item bar, the open container it was in, or the ground) and the new one is worn.
  - It works through the game's own moves, so the result (armour, warmth and so on) is the same as taking one off and putting the other on.
  - Only for your own drag or double-click. It does nothing, and the game's usual refusal shows, when either card cannot be moved by hand, when the worn card is mandatory equipment, or when the container the new card came from cannot take the old one (too heavy or filtered).

- **Sealed mark** (Display > Show Sealed Mark, off by default): when on, a small red wax seal is drawn in the top right corner of a sealed container's card, so a sealed one reads apart from an unsealed one on the board without opening either. It is display only: nothing about the cards changes.
  - Which cards count as sealed is the `Sealed Cards` list (Display), a comma-separated list of card IDs. The default is the game's Sealed Storage Pot, the placed Sealed Storage Pot and the Sealed Fermentation Bin. The game's own Sealed Container tag is not used, because it is also on the Unsealed Storage Pot; add the ID of a modded sealed container to mark it too.
  - The seal sits in the top right corner and the lock's padlock in the top left, so a locked sealed card shows both. It is drawn on the card wherever the card is shown, switches on and off within a frame of changing the option, and is never saved.

- **Randomise button** (Character > Show Randomise Button, on by default; it does nothing until pressed): on the character-creation perk screen a **Randomise** button sits under Submit. Each press re-rolls your perks for a challenge, or to adapt to what you get; press it again for another roll, or change the result by hand. Exactly what it does:
  - It keeps the difficulty rating band your character had before the press (the game's Relaxed, Controlled, Tense, Stressed, Overwhelmed, Desperate and Hopeless bands, each a wide range), takes at most one perk from each perk group, and only perks you have unlocked.
  - It rolls as many perks as you had equipped (not counting the ones the game always fills for you), or 4 to 8 when you had none.
  - It leaves alone the perks the game always fills (skin, hair, eyes, the start location) and the start place and start season groups, so where and when you start stay as you set them. It does not touch the character's name, bio or portrait.
  - Perks from a mod whose ModInfo says it is not for distribution (a development tool) are left out. If no roll lands in your band after 400 tries it keeps the closest and writes one warning to the BepInEx log.
  - It works through the screen's own equip and unequip, so the Equipped panel, the rating and the saved character all update as if you had clicked each perk yourself. A roll can include a kit perk you did not want; press it again or remove it by hand.

- **Auto-Pull Materials** (RepeatAction > Auto-Pull Materials, off by default): when on, a Repeat Action run that the game refuses because a card it needs **in your hand** is in a container instead takes one such card out into your hand and tries that repeat once more. The example in the base game is the Composite Bow's **Train**, which needs a Simple Arrow in hand: with your arrows in a chest or a bag, a repeated Train now fetches one instead of stopping. Exactly what it does:
  - Repeat Action already uses ingredients and targets that sit in a container where you are (Fibers thrown into a Compost Bin are taken out of a closed chest one by one, measured), so this option is only for the cards the game wants in your hand.
  - It looks in containers in the place you are in only: a chest or other structure on the ground, a bag you carry or wear. Never in another place, never in a construction site, a cooking pot or another small built-in inventory, a container you cannot take items out of, or an NPC's inventory.
  - It never takes a card you locked with `L`, or a card the game does not let you move by hand.
  - Nothing moves when your hands are full (no free slot in the carried row, or over your carry weight), so it never swaps a carried card into the container.
  - It takes one card, then lets Repeat Action try that repeat once more. If nothing suitable is found, or the repeat is still refused, the run stops exactly as it does without Utility Belt.
  - A card in an open container moves through the same right-click Quick Transfer replays; a card in a closed container moves through the game's own move that click ends in, after the same checks.
  - It needs the included Repeat Action and Quick Transfer, and works through two small public hooks in them (see Compatibility). With the option off Utility Belt does not listen to Repeat Action at all.

- **Store Products In** (RepeatAction > Store Products In, off by default; key Hotkeys > Pick Store Container, `V`): when on, hover a container (a chest on the ground, a bag you carry) and press `V` to make it the place Repeat Action puts what it makes. Each time a repeat finishes, every card it produced is moved into that container. Exactly what it does:
  - `V` on the same container again clears the choice. `V` on something that cannot take stored products (a cooking pot, a construction site, a small built-in inventory, anything not in the place you are in) shows a notice and changes nothing. Nothing happens while Store Products In is off or while you are typing in a text field.
  - The container is forgotten as soon as you change place (travel, even with a bag you carry), when it is destroyed, or when you turn the option off. Pick it again in the new place.
  - Only cards the repeated action made are moved, only items: never a liquid, a pinned card, a card you locked with `L`, a card the game does not let you move by hand, or one in an NPC's hands. A refuel from a pinned action is not a repeat, so what it makes is left alone.
  - It follows the game's own capacity rules (the container's filter, its weight limit and its slots). When the container is full, storing stops for the rest of that run and a notice says so; the repeats themselves carry on, and what they make stays where it lands. The next run tries again.
  - A product normally lands on the ground and is moved with the game's own drop into a container, whether the container is open or closed. Quick Transfer's right-click replay is used only for a product in your carried row while the destination's window is open.
  - It needs the included Repeat Action, and works through two small public events in it (see Compatibility). With the option off Utility Belt does not listen to Repeat Action at all.

- **Equip Spare Tool On Break** (RepeatAction > Equip Spare Tool On Break, off by default): when on, a Repeat Action run whose **worn** tool breaks puts on a spare and carries on. The base game's case is harvesting a wild Nettle Patch, which needs gloves worn: Leather Gloves wear out while you wear them, and when a pair breaks mid-run the next harvest is refused and the run stops. With the option on, a spare pair from your bag is put on and the run continues. Exactly what it does:
  - Repeat Action on its own does not notice a worn tool breaking (its Stop On Tool Break and Use Spare Tool On Break are for a tool you DRAG onto the target): the next repeat is simply refused by the game and the run stops with "requirements no longer met" (measured). This option answers that refusal.
  - The spare is a card that does the same job the broken tool did: the card, or a card with the tag, that the action needs worn, so it goes into the same equipment slot. When several fit, the one with the most use left is worn.
  - It looks in the place you are in only: in your hands, on the ground, or in a container here (a bag you carry or wear, a chest). Never in another place, never in a construction site, a cooking pot or another small built-in inventory, a container you cannot take items out of, or an NPC's inventory.
  - It never uses a card you locked with `L`, or a card the game does not let you move by hand.
  - It puts the spare on through the game's own equip move, the same one your drag onto the equipment line makes, so the game's own equip rules apply. If that slot is still taken by something else, the spare is put on only while Quickswap When Equipping is on (which then swaps the worn card out).
  - It puts on one spare per missing tool, then lets Repeat Action try that repeat once more. If nothing suitable is found, or the repeat is still refused, the run stops exactly as it does without Utility Belt.
  - It needs the included Repeat Action, and uses the same public hook as Auto-Pull Materials (see Compatibility). With the option off Utility Belt does not listen to Repeat Action for it.

It also prints one startup line to the BepInEx log and offers one diagnostic setting (see Configuration).

## Included mods

| Module | Version | Same as the standalone | Details |
|--------|---------|------------------------|---------|
| Repeat Action | 2.2.1 | Repeat Action 2.2.1 (`crispywhips.repeat_action`) | See Repeat Action's own page |
| Quick Transfer | 1.9.0 | Quick Transfer 1.9.0 (`crispywhips.quick_transfer`) | See Quick Transfer's own page |
| Skill Speed Boost | 1.11.0 | Skill Speed Boost 1.11.0 (`crispywhips.skill_speed_boost`) | See Skill Speed Boost's own page |

## Installation

### Requirements
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Card Survival: Fantasy Forest

Quick Transfer and Skill Speed Boost also need CSFF Mod Framework (`CSFF_Mod_Framework`); Repeat Action does not. No other mod is needed.

### Steps
1. Install BepInEx 5 for Card Survival: Fantasy Forest.
2. Extract the `Utility_Belt` folder into your `BepInEx/plugins/` directory.
3. Launch the game.

Your plugins folder should look like:
```
BepInEx/
  plugins/
    Utility_Belt/
      Utility_Belt.dll
      Repeat_Action.dll
      Quick_Transfer.dll
      Skill_Speed_Boost.dll
      Localization/
        SimpEn.csv
        SimpCn.csv
      ModInfo.json
```

## Usage

1. Do any action in the game (Forage, Clear, Chop, craft something).
2. Press `Shift+R` to repeat it 5 times (the default).
3. Use `Shift+Plus` or `Shift+Minus` beforehand to change the count.
4. Press `Shift+R` again to cancel a run.

### Default Keybinds

| Key Combination | Action |
|----------------|--------|
| `LeftShift+R` or `RightShift+R` | Repeat last action / cancel the repeat |
| `Shift+Plus` (`Shift+=` or `Shift+Keypad +`) | Increase repeat count |
| `Shift+Minus` (or `Shift+Keypad -`) | Decrease repeat count (below 1 = Unlimited) |
| `Shift+Mouse Wheel` | Increase or decrease repeat count |
| `Shift+P` | Pin the action `Shift+R` would replay now |
| `Shift+F` | Repeat the pinned action |
| `Shift+G` | Pause the running repeat after its current iteration; press again to resume |

Both Shift keys work for every control, and each key pair is configurable.

Quick Transfer uses these keys with a card under the pointer (full list on Quick Transfer's page):

| Key Combination | Action |
|----------------|--------|
| `Shift` + Right-Click | Transfer 5 cards (Shift amount) |
| `Ctrl` + Right-Click | Transfer 10 cards (Ctrl amount) |
| `Ctrl+Shift` + Right-Click | Transfer the whole stack (or half, or all but one) |
| `Ctrl` + `Plus` / `Minus` | Change the Ctrl amount |
| `Alt` + `Plus` / `Minus` | Change the Shift amount |
| Modifier + `X` | Same batch as the right-click, without clicking |
| Modifier + `Z` | Undo the last batch |

Utility Belt's own keys: `` ` `` (backquote) opens your next worn bag, `L` locks or unlocks the card under the pointer in an open container, and `O` re-orders the open container (see Utility Belt itself above).

`Shift+Plus` and `Shift+Minus` belong to Repeat Action's repeat count. Quick Transfer 1.9.0 reads Repeat Action's own config and ignores those keys while a Repeat Action modifier is held, so one press changes only the repeat count. This is a check of the two bundled DLLs' code, not a promise about other mods; if you rebind Repeat Action's count keys, Quick Transfer follows them live only with Repeat Action 2.2.1 or later (this version has it).

## Configuration

Repeat Action's settings live in `BepInEx/config/crispywhips.repeat_action.cfg`, created on first launch. The ones most players change:

| Setting | Default | Description |
|---------|---------|-------------|
| Default Repeat Count | 5 | The count the game starts with |
| Maximum Repeat Count | 50 | Upper limit for the count |
| Stop On Low Stats | on | Stop when a critical condition starts blocking actions |
| Show Count Indicator | off | Small label in the top-right corner showing the count and the next action |
| Use Spare Tool On Break | off | Carry on with another tool of the same kind when one breaks |
| Stop After In-Game Hours | 0 (off) | End a run after this much in-game time |
| Extra Blocked Actions | empty | Action names the mod must never capture |

Quick Transfer's settings live in `BepInEx/config/crispywhips.quick_transfer.cfg`. The ones most players change:

| Setting | Default | Description |
|---------|---------|-------------|
| Shift Preset Amount | 5 | Cards moved by Shift+Right-Click |
| Ctrl Preset Amount | 10 | Cards moved by Ctrl+Right-Click |
| Ctrl+Shift Preset Mode | All | Whole stack, half, or all but one |
| Drain Matching Stacks | off | Carry on into the container's other slots holding the same item |
| Full Stack Mode | off | Every modifier+right-click moves the whole stack |

Utility Belt's own config file is `BepInEx/config/crispywhips.utility_belt.cfg`:

**Accessibility**

| Setting | Default | Description |
|---------|---------|-------------|
| Hide Rain And Snow Effects | false | When true, hides the falling rain and snow (the game's weather-effect layers) while you are outside. The weather, its sound, its colour tint and its effects on your character are unchanged. Takes effect on the next frame, so it can be changed while playing |

**Display**

| Setting | Default | Description |
|---------|---------|-------------|
| Clock Format | TwentyFourHour | `TwentyFourHour` (the game's own 21:45) or `TwelveHour` (9:45p). Only the main clock changes; save-slot times and the log stay 24-hour. Takes effect within a frame of changing it |
| Show Capacity And Warmth Readouts | false | When true, the inspection popup's Info tab adds one line: a container's weight (and, for a pourable container, liquid) against its capacity in numbers, or a bed or cover's warmth while sleeping. Read from the card's own data; no comfort figure. Takes effect the next time a card is inspected |
| Show Sealed Mark | false | When true, a small red wax seal is drawn in the top right corner of a sealed container's card (a sealed storage pot or fermentation bin). Display only. Takes effect within a frame of changing it |
| Sealed Cards | the three sealed card IDs | A comma-separated list of the card IDs that get the sealed mark: the Sealed Storage Pot, the placed Sealed Storage Pot and the Sealed Fermentation Bin by default. Add a modded sealed container's ID to mark it too |

**Character**

| Setting | Default | Description |
|---------|---------|-------------|
| Show Randomise Button | true | When true, the character-creation perk screen has a Randomise button under Submit. Pressing it re-rolls your perks within the rating band you had, one perk per group, unlocked perks only, leaving skin, hair, eyes, start place and season as they are. It does nothing until pressed. Takes effect the next time the screen opens |

**Equipment**

| Setting | Default | Description |
|---------|---------|-------------|
| Quickswap When Equipping | false | When true, equipping a card whose equipment slot is full swaps it with the worn one: the worn card goes back to where the new one came from (item bar, open container or ground). Your own drag or double-click only. The game's refusal shows when either card cannot be moved by hand, the worn card is mandatory, or the container cannot take the old card |

**RepeatAction**

| Setting | Default | Description |
|---------|---------|-------------|
| Auto-Pull Materials | false | When true, a Repeat Action run refused because a card it needs in your hand is in a container (a chest on the ground, a bag you carry) in the place you are in takes one such card into your hand and tries that repeat once more. Locked cards, cards that cannot be moved by hand and cards in other places are never taken, and nothing moves when your hands are full. Takes effect within a frame of changing it |
| Equip Spare Tool On Break | false | When true, a Repeat Action run refused because a tool it needs worn is gone (it broke) puts on a spare that does the same job, from your hands, the ground or a container in the place you are in (the one with the most use left), and tries that repeat once more. Locked cards, cards that cannot be moved by hand and cards in other places are never used. A spare whose slot is still taken by something else is put on only while Quickswap When Equipping is on. If none is found, the run stops as it always has |
| Store Products In | false | When true, hover a container and press the Pick Store Container key to make it the destination: every card a Repeat Action run makes is moved into it as each repeat finishes. When it is full, storing stops for that run with a notice. The container is forgotten when you change place or turn the option off. Locked cards, cards that cannot be moved by hand and liquids are left alone. Takes effect within a frame of changing it |

**Hotkeys**

| Setting | Default | Description |
|---------|---------|-------------|
| Cycle Equipped Containers | BackQuote | Opens the next worn card that has an inventory, in equipment-slot order, wrapping around to the first. `None` turns it off |
| Open Equipped Container 1 to 5 | None | Opens the worn card with an inventory that is number 1 to 5 in that same order. Does nothing when fewer are worn |
| Lock Toggle | L | With a container open, locks or unlocks the card under the pointer inside it; Take out all items leaves a locked card where it is. `None` turns the key off, and locks you already set still apply |
| Sort Container | O | With a container open, each press changes its order: freshest first, then highest Quality first, then back to the order before the first press. Locked cards keep their slots. `None` turns the key off |
| Pick Store Container | V | With Store Products In on, hover a container and press this key to make it the destination for what Repeat Action makes; press it again on the same container to clear it. `None` turns the key off |

Keys are read when you are not typing in a text field, and take effect within a frame of changing them.

**Diagnostics**

| Setting | Default | Description |
|---------|---------|-------------|
| Log Module Sources | false | When true, logs once at startup which file Quick Transfer, Repeat Action and Skill Speed Boost each loaded from, or that one is not installed. For bug reports |

With the setting on, `BepInEx/LogOutput.log` gains one line per mod, of the form `<name> <version> from <folder>\<file>.dll`, or `<name>: not loaded`. The setting is read at startup, so change it and relaunch the game.

## Compatibility

- Card Survival: Fantasy Forest, BepInEx 5.4.23.4 or later. Needs no other mod; only the included Quick Transfer and Skill Speed Boost need CSFF Mod Framework.
- BepInEx keeps exactly one plugin per GUID, the one with the higher version. The included Repeat Action carries the standalone's GUID and settings, so with both installed only the newer copy runs and the settings in `crispywhips.repeat_action.cfg` are shared. At equal versions BepInEx picks one by file order. Each boot then logs one `Skipping [Repeat Action ...] because a newer version exists` Warning, which is harmless.
- To drop an included mod, delete its DLL from `Utility_Belt/`.
- The included Quick Transfer also carries the standalone's GUID and settings, so with both installed only the newer copy runs (BepInEx logs the same harmless `Skipping ... because a newer version exists` Warning). Installing the standalone Quick Transfer beside Utility Belt is therefore pointless; remove one of them.
- The included Skill Speed Boost likewise carries the standalone's GUID and settings, so with both installed only the newer copy runs. Install one of them, not both.
- Auto-Pull Materials talks to Repeat Action and Quick Transfer through two public hooks they carry from Repeat Action 2.2.1 and Quick Transfer 1.9.0: Repeat Action's `ActionPatch.OnRequirementsUnmet` event and Quick Transfer's `QuickTransferPatch.TryClickMoveOne(object card)`. Utility Belt finds them at run time by reflection, on whichever copy BepInEx loaded, so it takes no dependency on either and they take none on it. With an older copy of either mod the option stays off and the log says so once.
- Store Products In talks to Repeat Action through two more public events it carries from Repeat Action 2.2.1, `ActionPatch.OnIterationStarting` and `ActionPatch.OnIterationDone`, found the same way and only while the option is on. With an older Repeat Action the option stays off and the log says so once.
- Equip Spare Tool On Break subscribes its own handler to the same `ActionPatch.OnRequirementsUnmet` event as Auto-Pull Materials, only while the option is on; it needs no other hook in Repeat Action.

## Planned

Nothing more is planned for this version's features. Release work (a public 0.1.1) is still to come.

## Credits

Created by Jared (crispywhips)
Built with [BepInEx](https://github.com/BepInEx/BepInEx)
