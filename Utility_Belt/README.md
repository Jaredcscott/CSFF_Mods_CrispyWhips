# Utility Belt

**Version:** 0.1.5
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.69c)
**Requires:** BepInEx 5 (Quick Transfer and Skill Speed Boost also need CSFF Mod Framework)

Utility Belt gathers small quality-of-life mods into one download. It is in early development. It includes **Repeat Action** (repeat your last action with one key), **Quick Transfer** (move several cards at once) and **Skill Speed Boost** (tune how fast skills train), plus its own small options for display, hotkeys and inventory. Utility Belt adds no cards, items or blueprints.

Those three still ship as separate mods. Install Utility Belt if you would rather have one download. Delete a mod's DLL to remove it.

**Language:** English and Simplified Chinese

## Feature overview

| Part | Features |
|------|----------|
| Repeat Action 2.2.2 (included) | Repeat last action, adjustable count, travel repeat, safety stops, spare tool, pinned action, pause and resume |
| Quick Transfer 1.9.1 (included) | Modifier + right-click bulk transfer, adjustable amounts, tag filter, undo, transfer hotkey |
| Skill Speed Boost 1.11.1 (included) | Area Familiarity (on), XP multipliers, staleness, optional bonuses, difficulty profiles |
| Utility Belt itself | Hide rain and snow, 12-hour clock, worn-bag hotkeys, item lock, container sorting, capacity and warmth readouts, quickswap, sealed mark, character randomiser, card size and two-row card rows, extra hand slots, extra carry weight, hand and base row sorting, trash hotkey, carry-weight readout, worn tool mark, container filter, blueprint pins, and three Repeat Action helpers (fetch a card, store products, wear a spare tool) |

## Included mods

| Module | Version | Same as the standalone |
|--------|---------|------------------------|
| Repeat Action | 2.2.2 | Repeat Action 2.2.2 (`crispywhips.repeat_action`) |
| Quick Transfer | 1.9.1 | Quick Transfer 1.9.1 (`crispywhips.quick_transfer`) |
| Skill Speed Boost | 1.11.1 | Skill Speed Boost 1.11.1 (`crispywhips.skill_speed_boost`) |

### Repeat Action 2.2.2

- **Repeat Last Action**: press `Shift+R` to repeat your most recent action (Forage, Clear, Chop, Travel, a craft and so on). Press it again to cancel.
- **Adjustable count**: `Shift+Plus` and `Shift+Minus` (or `Shift+Mouse Wheel`) change the count. Go below 1 for Unlimited, which runs until a safety stop, a failed requirement or you cancel. A backstop (500 repeats by default) ends a run that can never fail.
- **Most actions work**: single actions, drag-and-drop crafts, stack buttons on piles, and group actions such as Eat All. Building a blueprint is not repeated.
- **Travel**: repeat a direction to keep moving. The run stops when there is no path forward.
- **Safety stops**: starving, dehydrated or exhausted; stat floors you set (Satiation, Hydration, Stamina, or any stat through `Extra Stat Thresholds`); a drag-drop tool breaking or dropping below a durability you choose. Optional stops: full containers, nightfall, a season change, a number of in-game hours, or holding a set quantity of a card.
- **Spare tool** (off by default): when a dragged tool breaks, carry on with another of the same kind from where you are.
- **Pinned action**: `Shift+P` pins the action `Shift+R` would replay, and `Shift+F` repeats it later, even after other actions or travel.
- **Pause and resume**: `Shift+G` pauses after the current repeat and resumes.
- **Feedback**: progress notifications with a fill bar, an optional count indicator in the top-right corner and an optional completion sound.
- **Honest stop reasons**: an early stop shows the game's own reason. It never answers an encounter for you.

Settings are in `BepInEx/config/crispywhips.repeat_action.cfg` and match the standalone Repeat Action 2.2.2, whose page lists every option, including presets, recorded sequences and refuel from a pinned action.

### Quick Transfer 1.9.1

- **Bulk transfer**: hold a modifier and **right-click** a card in an open container or your Item row. `Shift` moves 5 cards, `Ctrl` moves 10, and `Ctrl+Shift` moves the whole stack (or half, or all but one, by setting). Cards go the game's own way: out of a container into your Item row, or from your Item row into the open container.
- **Adjustable amounts**: `Ctrl` + `Plus`/`Minus` changes the Ctrl amount, `Alt` + `Plus`/`Minus` the Shift amount, and `Backspace` resets them. A label shows how many cards a click will move.
- **More ways to pick cards**: Tag Filter (hold `Caps Lock` to take every card sharing the clicked card's tag), Spoiled Only, Drain Matching Stacks, Prioritize Most Spoiled, a hover-and-press Transfer Hotkey (`X`), Undo (`Z`) and per-container settings.

Settings are in `BepInEx/config/crispywhips.quick_transfer.cfg` and match the standalone Quick Transfer 1.9.1, whose page lists every option.

**Quick Transfer needs CSFF Mod Framework.** Without it, bulk transfer does not work. Repeat Action and Utility Belt itself do not need it.

### Skill Speed Boost 1.11.1

**Out of the box, only one thing changes: Area Familiarity is on.** Working somewhere you have worked before earns up to +30% skill XP there, growing over your first 80 skill-training actions in that place. Everything else stays vanilla: skills go stale as in the base game, XP multipliers are 1, and every other bonus is off until you turn it on. To keep XP fully vanilla, delete `Skill_Speed_Boost.dll` from `Utility_Belt/` or set `AreaFamiliarityEnabled = false` in `BepInEx/config/crispywhips.skill_speed_boost.cfg`.

- **XP multipliers**: one global multiplier (1 to 10) and one per skill, including skills other mods add.
- **Staleness**: keep the game's staleness penalty, or switch it off.
- **Optional bonuses** (all off): morning and night windows, daily first use, well-rested, in-the-flow, day streak, rested bank, milestones, level scaling, skill synergies, and bonus maps by station, season, place, tool and action.
- **Difficulty profiles**: named presets (Balanced, Casual, Hardcore and others) that set everything with one key.

Settings and familiarity records (`BepInEx/config/SkillSpeedBoost/`) are shared with the standalone mod, whose page lists every option. It needs CSFF Mod Framework for staleness, familiarity and the morning bonus.

## Utility Belt's own features

- **Hide Rain And Snow Effects** (Accessibility, off by default): hides the falling rain and snow drawn over the screen outside, for example if flashing weather bothers you. The weather card, its sound, its tint and its effects on your character stay as they are. Clear weather has nothing to hide.

- **Clock Format** (Display, `TwentyFourHour` by default): set `TwelveHour` to show the main clock as "9:45p" instead of "21:45" (`a` for morning, `p` for afternoon and evening; midnight is 12:00a, noon is 12:00p). Save-slot times and the log stay 24-hour. Simplified Chinese shows 上午 or 下午 before the time. The text shrinks a little if it would be wider than the 24-hour clock.

- **Worn-bag hotkeys** (Hotkeys): the backquote key (`` ` ``, left of `1`) opens your next worn bag, sack or other worn card with an inventory, in equipment-row order, wrapping back to the first. It opens the bag as a click does, so nothing opens while an action is running. With only one bag worn, pressing it again closes that bag. A worn knife sheath or spear carrier counts as a bag. Five more keys, Open Equipped Container 1 to 5, open one bag directly; they are off (`None`) by default. Avoid the number keys `1` to `9` for them: the game uses those for bookmarks.

- **Item lock** (Hotkeys, `L` by default): open a container, rest the pointer on a card inside it and press `L`. A small padlock appears, and the game's **Take out all items** button leaves that card where it is. Press `L` again to unlock.
  - Take out all items, Quick Transfer batches and Repeat Action runs all honour a lock. A run never picks up a locked card as a new target or ingredient, though the exact card you used for the first repeat stays eligible. Dragging a card yourself ignores the lock.
  - A lock belongs to one card, told apart by its kind and the moment it was made. Cards that arrived together (one harvest, one craft) lock and unlock together. Two of the same item made at different times lock separately.
  - A card that changes into something else (cooked, dried, broken) is a new card, so the lock does not follow it.
  - In a pile you only see the top card's padlock.
  - Locks are saved per save slot in `BepInEx/config/UtilityBelt/ItemLocks_Slot<N>.tsv`, not in the game's save. Locks on cards that no longer exist are dropped when you load, and a new game in a slot clears that slot's locks.
  - Loading a saved recipe into a container, or switching or taking down a blueprint, still empties it completely.

- **Sort Container** (Hotkeys, `O` by default): with a container open, each press changes its order. First freshest food (by spoilage bar), then highest Quality, then back to the order before your first press. The next press starts over.
  - It reorders slots inside the open container and cards inside each pile. No card leaves. A locked card keeps its slot.
  - Cards of one kind always share one pile, because the game merges them whenever a container opens. A pile is ranked by its best card. Cards with no spoilage bar sort last under freshness, and cards with no Quality bar sort last under Quality.
  - The order you leave a container in survives saving, loading and closing. Each container also remembers where it is in the cycle, so you can close it, open another and come back to carry on. That memory lasts until you load a save or start a run.
  - It does nothing in small built-in inventories (a Cooking Pot, for example), cooking containers, fixed-slot stations or containers holding a saved recipe.

- **Capacity and warmth readouts** (Display > Show Capacity And Warmth Readouts, off by default): adds one line under a card's description on its Info tab. The game shows a container's fill only as a bar, and shows no warmth figure for beds or covers.
  - A container with a weight capacity shows `Weight: 1250 / 4000` in the game's weight units, liquid included.
  - A container you pour into (a bowl, jar, pot, cauldron) also shows `Liquid: 300 / 300`. Clothing, tools and arrows never show a liquid line.
  - A bed, shelter or hide shows `Warmth while sleeping: +2`, the temperature it gives while you sleep. With several such effects it shows the largest, and the real figure can be lower if the card's other conditions are not met. Worn clothing shows no line, and there is no comfort figure.
  - The line appears on the Info tab only, in Chinese too when the game is in Chinese.

- **Quickswap** (Equipment > Quickswap When Equipping, off by default): equipping a card into a full slot swaps with the worn one instead of refusing. The worn card goes back where the new one came from (item bar, open container or ground). The result is the same as taking one off and putting the other on.
  - Only for your own drag or double-click.
  - The game's refusal still shows if either card cannot be moved by hand, the worn card is mandatory equipment, or the container the new card came from cannot take the old one (too heavy or filtered).

- **Sealed mark** (Display > Show Sealed Mark, off by default): draws a small red wax seal in the top right corner of a sealed container's card, so you can tell it from an unsealed one without opening either. Display only.
  - `Sealed Cards` (Display) is a comma-separated list of card IDs that get the mark: the Sealed Storage Pot, the placed Sealed Storage Pot and the Sealed Fermentation Bin by default. Add a modded container's ID to mark it too. The game's Sealed Container tag is not used, because the Unsealed Storage Pot has it as well.
  - The seal sits top right and the lock's padlock top left, so a locked sealed card shows both.

- **Randomise button** (Character > Show Randomise Button, on by default, does nothing until pressed): on the character-creation perk screen, a **Randomise** button under Submit re-rolls your perks. Press again for another roll, or adjust by hand.
  - It keeps your difficulty band (Relaxed, Controlled, Tense, Stressed, Overwhelmed, Desperate or Hopeless), takes at most one perk per group, and only perks you have unlocked.
  - It rolls as many perks as you had equipped (not counting the ones the game always fills), or 4 to 8 if you had none.
  - Skin, hair, eyes, start location and start season stay as you set them. Name, bio and portrait are untouched.
  - Perks from mods marked not for distribution are left out. If no roll lands in your band after 400 tries, it keeps the closest and logs one warning.
  - A roll can include a kit perk you did not want; press again or remove it by hand.

- **Auto-Pull Materials** (RepeatAction > Auto-Pull Materials, off by default): when the game refuses a repeat because a card it needs **in your hand** is in a container, one such card is taken into your hand and the repeat tries once more. The base game's example is the Composite Bow's **Train**, which needs a Simple Arrow in hand: with arrows in a chest or bag, a repeated Train fetches one instead of stopping.
  - Repeat Action already uses ingredients and targets that sit in a container where you are, so this is only for cards the game wants in your hand.
  - It looks only in the place you are: a chest or structure on the ground, or a bag you carry or wear. Never another place, a construction site, a cooking pot or other small built-in inventory, a container you cannot take from, or an NPC's inventory.
  - It never takes a locked card or one the game will not let you move by hand, and nothing moves when your hands are full.
  - If nothing suitable is found, or the repeat is still refused, the run stops as it does without Utility Belt.
  - Needs the included Repeat Action and Quick Transfer.

- **Store Products In** (RepeatAction > Store Products In, off by default; key Hotkeys > Pick Store Container, `V`): hover a container (a chest on the ground, a bag you carry) and press `V`. Each time a repeat finishes, everything it made goes into that container.
  - `V` on the same container clears the choice. On something that cannot take products (a cooking pot, a construction site, a small built-in inventory, anything outside your place) it shows a notice and changes nothing.
  - The container is forgotten when you change place (even with a bag you carry), when it is destroyed, or when you turn the option off. Pick it again in the new place.
  - Only items the repeated action made are moved: never a liquid, a pinned card, a locked card, a card the game will not let you move by hand, or one in an NPC's hands.
  - The container's own filter, weight limit and slots apply. When it is full, storing stops for the rest of that run with a notice; the repeats carry on and products stay where they land.
  - Needs the included Repeat Action.

- **Equip Spare Tool On Break** (RepeatAction > Equip Spare Tool On Break, off by default): when a **worn** tool breaks mid-run, a spare is put on and the run carries on. The base game's example is harvesting a wild Nettle Patch, which needs gloves worn: when a pair of Leather Gloves breaks, the next harvest is refused and the run stops. With this on, a spare pair from your bag goes on.
  - Repeat Action's own spare-tool setting only covers tools you drag onto the target. Without this option a worn tool breaking stops the run with "requirements no longer met".
  - The spare does the same job as the broken tool and goes in the same slot. With several to choose from, the one with the most use left is worn.
  - It looks only in the place you are: your hands, the ground, or a container here. Never another place, a construction site, a cooking pot or other small built-in inventory, a container you cannot take from, or an NPC's inventory.
  - It never uses a locked card or one the game will not let you move by hand.
  - If the slot is still taken by something else, the spare goes on only while Quickswap When Equipping is on.
  - It puts on one spare per missing tool, then the repeat tries once more. If nothing suitable is found, the run stops as it does without Utility Belt.
  - Needs the included Repeat Action.

- **Card Size** (every row `Normal` by default): fit more cards on screen. Each of eight rows has its own setting: the location row at the top, the base row of cards on the ground, your hand, the blueprint row, an open container, the equipment row in the character window, and the trade window's inventory and offer rows.
  - `Smaller` draws one row of smaller cards (Smaller Size, 0.7 by default), so more fit across.
  - `TwoRows` stacks half-size cards in two rows, using the game's own multi-row layout (the one its zoom button uses).
  - `TwoRowsWhenCrowded` stays normal until the row holds more cards than Crowded Above (9), then switches to two rows, and back to one once it holds that many or fewer.
  - Display only: no card moves. A row set back to `Normal` returns to exactly how the game drew it, and changes apply within a frame.
  - A row the game's own zoom button also controls follows the button again whenever its setting is `Normal`.
  - This replaces the Card Size Reduce mod, with no ModCore needed.

- **Extra Hand Slots** (Capacity, 0 by default): adds 3, 6 or 9 slots to your item bar. Takes effect the next time you load a save or start a run. Empty the extra slots before lowering it.

- **Extra Carry Weight** (Capacity, 0 by default): raises the most weight you can carry by 3000, 6000 or 9000 (the base limit is 4000). Applies to the run in play as soon as you change it, and to every run you start or load. Nothing is written to your save, so turning it off or removing Utility Belt puts the limit back.

The six options below are new in 0.1.4. Every one is off, or has no key, until you turn it on in the config file.

- **Sort Hand Row and Sort Base Row** (Hotkeys, no key by default): each press re-orders your item bar, or the base row of cards on the ground, the same way Sort Container does. First freshest, then highest Quality, then back to the order before your first press.
  - Locked and pinned cards keep their places, and empty item-bar slots stay at the end.
  - The new order is kept when you save.
  - The keys do nothing while you type, while an action runs or while you drag a card.

- **Trash Card** (Hotkeys, no key by default): rest the pointer on a card and press the key to trash ONE card, exactly as the trash button in its window does.
  - Only cards that button would offer: never a pinned card or a card the game will not let you trash.
  - A card you locked is never trashed.
  - The key does nothing while you type, while an action runs or while you drag a card.

- **Carry weight readout** (Display > Show Carry Weight, off by default): hovering the weight bar beside your item bar shows how much you carry against your limit in numbers, for example "Carrying 500 / 4000". The limit includes Extra Carry Weight. The game itself shows only the bar.

- **Worn tool mark** (Display > Show Worn Tool Mark, off by default): a small cracked amber mark in the bottom left corner of any tool, piece of clothing or other gear whose Usage bar is below Worn Tool Threshold (25 percent by default), so a tool about to break stands out. Food, wood, leather and animals, whose bar counts drying, fragrance, flexibility or thirst, are never marked. Display only.

- **Container filter** (Display > Container Filter, off by default; key Hotkeys > Focus Container Filter, none by default): an open container's window gets a search box in its tab bar.
  - Type part of a card's name, in the language you play in, and only matching slots are shown.
  - Clearing the box, closing the window or opening another container shows every slot again.
  - While the box has the cursor, the game's keys and Utility Belt's keys stay quiet.
  - It only hides slots: no card moves, and Take out all items still empties the whole container (locked cards stay).

- **Blueprint pins** (Hotkeys > Pin Blueprint, no key by default): in the blueprint screen, rest the pointer on a blueprint and press the key to pin it.
  - A pinned blueprint is shown first in its tab and in Search, with a small red pin.
  - Press the key again to unpin it.
  - Pins are kept in the config (General > Pinned Blueprints) for every save. Research and building are unchanged.
  - The key does nothing while Search's own box has the cursor; pin from a normal tab.

## Installation

Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Card Survival: Fantasy Forest. Quick Transfer and Skill Speed Boost also need CSFF Mod Framework (`CSFF_Mod_Framework`); Repeat Action does not. No other mod is needed.

1. Extract the `Utility_Belt` folder into `BepInEx/plugins/`.
2. Launch the game.

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

### Default keys

| Key | Action |
|-----|--------|
| `Shift+R` (either Shift) | Repeat last action / cancel the repeat |
| `Shift+Plus` (`Shift+=` or `Shift+Keypad +`) | Increase repeat count |
| `Shift+Minus` (or `Shift+Keypad -`) | Decrease repeat count (below 1 = Unlimited) |
| `Shift+Mouse Wheel` | Increase or decrease repeat count |
| `Shift+P` | Pin the action `Shift+R` would replay |
| `Shift+F` | Repeat the pinned action |
| `Shift+G` | Pause the running repeat after its current iteration; press again to resume |

Each key pair is configurable.

Quick Transfer, with a card under the pointer (full list on Quick Transfer's page):

| Key | Action |
|-----|--------|
| `Shift` + Right-Click | Transfer 5 cards |
| `Ctrl` + Right-Click | Transfer 10 cards |
| `Ctrl+Shift` + Right-Click | Transfer the whole stack (or half, or all but one) |
| `Ctrl` + `Plus` / `Minus` | Change the Ctrl amount |
| `Alt` + `Plus` / `Minus` | Change the Shift amount |
| Modifier + `X` | Same batch as the right-click, without clicking |
| Modifier + `Z` | Undo the last batch |

Utility Belt's own keys: `` ` `` (backquote) opens your next worn bag, `L` locks or unlocks the card under the pointer, `O` re-orders the open container, and `V` picks the container Store Products In fills. Sort Hand Row, Sort Base Row, Trash Card, Focus Container Filter and Pin Blueprint have no key until you set one. Avoid the game's own keys (A, B, C, D, E, H, J, Q, S, T, W), Quick Transfer's (X, Z), Repeat Action's (G, N, K, M, U, R, P, F) and the number keys.

`Shift+Plus` and `Shift+Minus` belong to Repeat Action. The included Quick Transfer ignores those keys while a Repeat Action modifier is held, so one press changes only the repeat count. If you rebind Repeat Action's count keys, Quick Transfer follows them (Repeat Action 2.2.1 or later). This is a check of the two bundled DLLs, not a promise about other mods.

## Configuration

Every setting on this page can be changed in game with the optional [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) (BepInEx 5 build): press F1 and look under "Repeat_Action", "Quick_Transfer", "Skill Speed Boost" and "Utility Belt". A change made there applies at once, except `Extra Hand Slots` (next save load or new run), `Log Module Sources` (next launch) and Repeat Action's `Default Repeat Count` (sets the count the game starts with).

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
| Hide Rain And Snow Effects | false | Hides the falling rain and snow while you are outside. The weather, its sound, its tint and its effects on your character are unchanged. Works while playing |

**Display**

| Setting | Default | Description |
|---------|---------|-------------|
| Clock Format | TwentyFourHour | `TwentyFourHour` (21:45) or `TwelveHour` (9:45p). Only the main clock changes. Works while playing |
| Show Capacity And Warmth Readouts | false | Adds one line to a card's Info tab: a container's weight (and liquid, for pourable ones) against capacity, or a bed or cover's warmth while sleeping. Applies the next time a card is inspected |
| Show Sealed Mark | false | Draws a small red wax seal on a sealed container's card. Display only. Works while playing |
| Sealed Cards | the three sealed card IDs | Comma-separated card IDs that get the sealed mark: the Sealed Storage Pot, the placed Sealed Storage Pot and the Sealed Fermentation Bin by default. Add a modded container's ID to mark it too |
| Show Carry Weight | false | Hovering the weight bar shows what you carry against your limit in numbers. Works while playing |
| Show Worn Tool Mark | false | Draws a small cracked mark on a tool whose Usage is below Worn Tool Threshold. Display only. Works while playing |
| Worn Tool Threshold | 25 | The Usage percentage below which a tool gets the worn mark (1 to 99) |
| Container Filter | false | Adds a search box to an open container's window; only slots whose card name holds the text are shown. Applies the next time a container opens |

**Card Size**

| Setting | Default | Description |
|---------|---------|-------------|
| Location Row, Base Row, Hand Row, Blueprint Row, Container Row, Equipment Row, Trade Inventory Row, Trade Offer Row | Normal | `Normal`, `Smaller` (one row of smaller cards), `TwoRows` (two rows of half-size cards) or `TwoRowsWhenCrowded` (two rows only while the row holds more cards than Crowded Above). Display only. Works while playing |
| Crowded Above | 9 | A `TwoRowsWhenCrowded` row switches to two rows while it holds more cards than this (1 to 50) |
| Smaller Size | 0.7 | How big cards are drawn in a `Smaller` row, as a fraction of normal (0.4 to 0.95) |

**Capacity**

| Setting | Default | Description |
|---------|---------|-------------|
| Extra Hand Slots | 0 | Slots added to your item bar: 0, 3, 6 or 9. Applies the next time you load a save or start a run |
| Extra Carry Weight | 0 | Added to the most weight you can carry: 0, 3000, 6000 or 9000. Works while playing; nothing is saved |

**Character**

| Setting | Default | Description |
|---------|---------|-------------|
| Show Randomise Button | true | Adds a Randomise button under Submit on the character-creation perk screen. It does nothing until pressed. Applies the next time the screen opens |

**Equipment**

| Setting | Default | Description |
|---------|---------|-------------|
| Quickswap When Equipping | false | Equipping into a full slot swaps with the worn card instead of refusing. Your own drag or double-click only |

**RepeatAction**

| Setting | Default | Description |
|---------|---------|-------------|
| Auto-Pull Materials | false | A repeat refused because a card it needs in your hand is in a chest or bag where you are takes one into your hand and tries once more. Locked cards are never taken, and nothing moves when your hands are full. Works while playing |
| Equip Spare Tool On Break | false | A repeat refused because a worn tool broke puts on a spare that does the same job, from your hands, the ground or a container where you are, and tries once more. Locked cards are never used. Works while playing |
| Store Products In | false | Hover a container and press the Pick Store Container key: every card a Repeat Action run makes goes into it as each repeat finishes. Works while playing |

**Hotkeys**

| Setting | Default | Description |
|---------|---------|-------------|
| Cycle Equipped Containers | BackQuote | Opens the next worn card with an inventory, wrapping around. `None` turns it off |
| Open Equipped Container 1 to 5 | None | Opens the worn card with an inventory that is number 1 to 5 in the same order. Does nothing when fewer are worn |
| Lock Toggle | L | Locks or unlocks the card under the pointer in an open container. `None` turns the key off; locks you already set still apply |
| Sort Container | O | Freshest first, then highest Quality, then back to the earlier order. Locked cards keep their slots. `None` turns the key off |
| Pick Store Container | V | With Store Products In on, hover a container and press to make it the destination; press again to clear. `None` turns the key off |
| Sort Hand Row | None | Freshest first, then highest Quality, then back to the earlier order, for your item bar. Locked and pinned cards keep their places |
| Sort Base Row | None | The same for the base row of cards on the ground |
| Trash Card | None | Trashes one card under the pointer, as its trash button does. Never a pinned, untrashable or locked card |
| Focus Container Filter | None | With Container Filter on and a container open, puts the cursor in its search box |
| Pin Blueprint | None | In the blueprint screen, pins or unpins the blueprint under the pointer |

Keys are ignored while you type in a text field, and changes apply within a frame.

**General**

| Setting | Default | Description |
|---------|---------|-------------|
| Pinned Blueprints | empty | Comma-separated blueprint IDs Pin Blueprint has pinned. Shared by every save |

**Diagnostics**

| Setting | Default | Description |
|---------|---------|-------------|
| Log Module Sources | false | Logs once at startup which file Quick Transfer, Repeat Action and Skill Speed Boost each loaded from, or that one is not installed. For bug reports. Change it and relaunch |

With it on, `BepInEx/LogOutput.log` gains one line per mod: `<name> <version> from <folder>\<file>.dll`, or `<name>: not loaded`.

## Compatibility

- Card Survival: Fantasy Forest, BepInEx 5.4.23.4 or later. Only the included Quick Transfer and Skill Speed Boost need CSFF Mod Framework.
- Each included mod carries its standalone's name and settings. If you install both the standalone and the included copy, only the newer one runs, and BepInEx logs a harmless `Skipping ... because a newer version exists` warning each boot. At equal versions BepInEx picks by file order. Install one or the other.
- To drop an included mod, delete its DLL from `Utility_Belt/`.
- Card Size, Extra Hand Slots and Extra Carry Weight replace Pikachu's Card Size Reduce. While Card Size Reduce is installed they stay off, so the two never shrink one row twice, and the log says so once. Remove Card Size Reduce (and ModCore, if nothing else of yours needs it) to use them. Card Size Reduce's own perks are not carried over: set the two Capacity options instead.
- Auto-Pull Materials, Store Products In and Equip Spare Tool On Break work through small public hooks that Repeat Action 2.2.1 and Quick Transfer 1.9.0 and later carry. Utility Belt finds them at run time on whichever copy BepInEx loaded, so none of these mods depends on another. With an older copy the option stays off and the log says so once.

## Version history

Full notes are in `CHANGELOG.md`.

- **0.1.5**: the worn tool mark no longer marks food, wood, leather or animals.
- **0.1.4**: Sort Hand Row and Sort Base Row, Trash Card, carry-weight readout, worn tool mark, container filter and blueprint pins (all off until turned on), and Sort Container remembers each container's place in its cycle.
- **0.1.3**: Card Size (smaller or two-row card rows), Extra Hand Slots and Extra Carry Weight, replacing Card Size Reduce.
- **0.1.2**: version number only.
- **0.1.1**: Quick Transfer and Skill Speed Boost included, plus every Utility Belt feature above.
- **0.1.0**: Repeat Action included; the plugin loads.

## Credits

Created by Jared (crispywhips)
Built with [BepInEx](https://github.com/BepInEx/BepInEx)
