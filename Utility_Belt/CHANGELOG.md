# Utility Belt - Changelog

All notable changes to this mod are documented here.

## [0.1.4] - 2026-10-17

### Added
Every new option is off, or has no key, until you set it.

- `Hotkeys > Sort Hand Row` and `Hotkeys > Sort Base Row` re-order your item bar or the base row the way Sort Container does: each press steps through freshest first, highest Quality first, then back to the order before your first press. Locked and pinned cards keep their places, empty item-bar slots stay at the end, and the new order is kept when you save.
- `Hotkeys > Trash Card` trashes the one card under the pointer, exactly as its trash button does. It never trashes a pinned card, a card the game will not let you trash, or a card you locked.
- `Display > Show Carry Weight`: hover the weight bar beside your item bar to read what you carry against your limit, in numbers. The limit includes Extra Carry Weight.
- `Display > Show Worn Tool Mark` and `Worn Tool Threshold` (25 percent): a small cracked mark on a tool whose Usage is below the threshold.
- `Display > Container Filter` and `Hotkeys > Focus Container Filter`: a search box in an open container's window shows only the slots whose card name holds what you type. While the box has the cursor, game and Utility Belt keys stay quiet.
- `Hotkeys > Pin Blueprint` and `General > Pinned Blueprints`: pin a blueprint to show it first in its tab and in Search, with a small pin mark. Pins are kept for every save; research is unchanged.

### Changed
- Sort Container now remembers where each container is in its cycle until you load a save or start a run. Closing a container, opening another and coming back carries on from the same step instead of starting at freshest again. There is no option for this.

## [0.1.3] - 2026-10-14

### Added
- `Card Size`: each of eight card rows (location, base, hand, blueprint, open container, equipment, trade inventory, trade offer) can be `Normal`, `Smaller` (one row of smaller cards; `Smaller Size`, 0.7 by default), `TwoRows` (two rows of half-size cards) or `TwoRowsWhenCrowded` (two rows only while the row holds more cards than `Crowded Above`, 9 by default). Every row is `Normal` by default. Two rows use the game's own multi-row layout, and a row set back to `Normal` returns to exactly how the game drew it. This replaces the Card Size Reduce mod and its ModCore requirement.
- `Capacity > Extra Hand Slots` (0, 3, 6 or 9; 0 by default) adds slots to your item bar from the next load.
- `Capacity > Extra Carry Weight` (0, 3000, 6000 or 9000; 0 by default) raises the most weight you can carry, straight away and on every run. Nothing is saved, so turning it off puts the limit back.

### Notes
- While Card Size Reduce is installed, these options stay off and the log says so once.

## [0.1.2] - 2026-10-12

Version number only. Everything new since the last Nexus release (0.1.0) is listed under 0.1.1 below.

## [0.1.1] - Unreleased

### Added
- Quick Transfer and Skill Speed Boost are now included, each under its own name and settings, so nothing the standalone mods installed is replaced. Both need CSFF Mod Framework, as the standalone mods do.
- Skill Speed Boost's Area Familiarity XP bonus is on out of the box, as in the standalone. To keep XP vanilla, delete `Skill_Speed_Boost.dll` from `Utility_Belt/`.
- `Accessibility > Hide Rain And Snow Effects` (off by default) hides the falling rain and snow while you are outside. The weather, its sound, its tint and its effect on your character stay as they were.
- `Display > Clock Format` (`TwentyFourHour` by default, or `TwelveHour`) shows the main clock as "9:45p" instead of "21:45". Save-slot times and the log stay 24-hour.
- `Hotkeys > Cycle Equipped Containers` (backquote by default) opens your next worn bag, sack or sheath, wrapping back to the first. `Open Equipped Container 1` to `5` (off by default) open one directly.
- Item lock: `Hotkeys > Lock Toggle` (`L` by default). Rest the pointer on a card in an open container and press the key to lock it; a small padlock appears. Take out all items, Quick Transfer batches and Repeat Action runs leave locked cards alone, and dragging by hand still works. Locks are saved per save slot, and a card that turns into another card loses its lock. Other mods can check a lock through `Utility_Belt.LockApi.IsLocked(object card)`.
- Sort Container: `Hotkeys > Sort Container` (`O` by default). Each press re-orders the open container: freshest food first, then highest Quality, then back to the original order. No card leaves the container, and locked cards keep their slots. It does nothing in small built-in inventories, cooking containers, fixed-slot stations or containers holding a saved recipe.
- `Display > Show Capacity And Warmth Readouts` (off by default) adds one line to a card's Info tab: a container's weight (and liquid, for bowls, jars and pots) against its capacity, or a bed or cover's warmth while sleeping.
- `Equipment > Quickswap When Equipping` (off by default): equipping into a full slot swaps with the worn card, which goes back where the new one came from. It works for your own drag or double-click only.
- `Display > Show Sealed Mark` (off by default) draws a small red wax seal on a sealed container's card. `Display > Sealed Cards` lists which cards count as sealed (the Sealed Storage Pot, the placed Sealed Storage Pot and the Sealed Fermentation Bin by default); add a modded container's ID to mark it too.
- `Character > Show Randomise Button` (on, and it does nothing until pressed) adds a **Randomise** button under Submit on the character-creation perk screen. Each press re-rolls your perks within your difficulty band, one perk per group, unlocked perks only. Skin, hair, eyes, start place and start season are left alone.
- `RepeatAction > Auto-Pull Materials` (off by default): when a repeat is refused because a card it needs in your hand sits in a chest or bag where you are, one is taken into your hand and the repeat tries once more. The base game's example is the Composite Bow's Train, which needs a Simple Arrow in hand.
- `RepeatAction > Store Products In` (off by default), with `Hotkeys > Pick Store Container` (`V`): hover a container and press `V`, and everything a Repeat Action run makes goes into it as each repeat finishes. When it is full, storing stops with a notice and the run carries on. The container is forgotten when you change place.
- `RepeatAction > Equip Spare Tool On Break` (off by default): when a tool you wear breaks mid-run (worn Leather Gloves while harvesting nettles), a spare that does the same job is put on and the run carries on. Repeat Action's own spare-tool setting only covers tools you drag.

### Changed
- The included Repeat Action, Quick Transfer and Skill Speed Boost are now the current builds (2.2.2, 1.9.1 and 1.11.1). Repeat Action's new options (presets, recorded sequences, refuel from a pinned action) are listed on its own page.
- Utility Belt applies Harmony patches for the first time (weather, clock, Info tab, equip and character creation), and Repeat Action, Quick Transfer and Utility Belt find each other by reflection, so none depends on another being installed.

## [0.1.0] - 2026-10-01

### Added
- Repeat Action is included: its own build ships beside `Utility_Belt.dll` under its own GUID, so it replaces nothing and shares the standalone's settings.
- The scaffold: the host plugin loads and prints its one startup line. No features.
- `Diagnostics > Log Module Sources` (default off): when on, logs once at startup which file Quick Transfer, Repeat Action and Skill Speed Boost each loaded from, or that one is not installed.
