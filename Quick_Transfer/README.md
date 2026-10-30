# Quick Transfer

**Version:** 1.9.3
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.69c)
**Language:** English and Chinese (on-screen text)
**Requires:** BepInEx 5.4.23.4+ and CSFFModFramework

Hold a modifier key and right-click a card stack to move several cards at once, in or out of the open container.

> **Using Repeat Action too?** Shift+Plus/Minus now belongs to Repeat Action's repeat count. Nudge Quick Transfer's Shift preset with **Alt+Plus/Minus** instead. See [Compatibility](#compatibility).

## Quick start

1. Open a container, or open nothing (see "With no container open" below).
2. Hold a modifier and right-click a stack:
   - **Shift** moves 5 cards.
   - **Ctrl** moves 10 cards.
   - **Ctrl+Shift** moves the whole stack. A setting can make it half the stack or all but one card.
3. An on-screen overlay shows how many cards will move before you click.

Direction follows the card you click. A card in your Item row goes into the open container. A card inside the container comes out to your Item row. The card you click moves first, because that move is the game's own. Quick Transfer moves the rest to the same place.

## Features

- **Adjustable presets.** Hold Ctrl and press Plus or Minus to change the Ctrl amount. Hold Alt and press Plus or Minus to change the Shift amount. Without Repeat Action, Shift+Plus/Minus also changes the Shift amount. Changes save to the config. Hold a modifier and press Backspace to reset to 5 / 10 / 5.
- **Half and All But One.** Set `Ctrl+Shift Preset Mode` to `Half` to move half the clicked stack, rounded up (8 moves 4, 7 moves 4, 1 moves 1). Set it to `AllButOne` to leave one card behind, such as one torch in your Item row. A stack of one does not move in that mode.
- **Full Stack Mode.** Set `Full Stack Mode = true` and any modifier+right-click moves the whole stack.
- **Legacy Custom Mode.** Set `Enable Modifier Presets = false` to use one modifier key and one custom amount, as in version 1.5.x.
- **Middle-click trigger.** Set `Transfer Mouse Button = Middle` to move bulk transfers to middle-click. Modifier+right-click is then the game's normal one-card move. Left-click is not offered, because the game uses it for inspection.
- **One sound per batch.** The game plays a sound for every card moved. Quick Transfer mutes those and plays one when the batch ends. Turn this off with `Consolidate Batch Sound = false`.
- **Drain Matching Stacks** (off by default). When the clicked slot empties, the batch carries on with the same item from the container's other slots, up to the requested count.
- **Cards that cannot pile.** Some cards never share a slot, such as the Water-Driven Workshop's storage, cooking containers, tools and baskets. With `Widen For Non-Piling Cards` (on by default), a batch on one of these carries on into the same item's other slots, up to the requested count. Set it to `false` for one-card moves.
- **Tag Filter.** Hold **Caps Lock** with the modifier and the batch widens to every card in the container that shares the clicked card's tag. The default list covers food for people and raw food (raw meat, fish, organs, eggs). The start notice names the tag, for example `Quick Transfer: 10 (Human Food)`. A card with none of the listed tags moves alone. Caps Lock also toggles your keyboard's Caps Lock. Set `Tag Filter Key` to another key, or `None`, if that gets in the way.
- **Spoiled Only.** Hold **Left Alt** with the modifier+click to move only cards at least half way to spoiling. Change the cutoff with `Spoiled Only Threshold` (1 to 100, default 50). It works across every slot holding that item. If the card you click is fresher, it stays put and the batch moves the older ones. Cards that cannot spoil never move. Add Caps Lock to cover every food in the container.
- **Prioritize Most Spoiled** (off by default). When a batch draws on several stacks, the stack closest to spoiling goes first.
- **Transfer Hotkey.** Hold a modifier, rest the pointer on a card and press **X** for the same batch without clicking. Useful for one-handed play, trackpads, or a controller button mapped to X through Steam Input. It does nothing while a text box has focus. `None` turns it off.
- **Undo Key.** Hold a modifier and press **Z** to move the last batch back while its container is still open. The overlay reads `Undid 10`. One level only: a second press, a new batch, or closing the container leaves `Nothing to undo`. It restores how many of each item, not the identical cards. If the chest already held some of that item, different cards may come back. `None` turns it off.
- **Per-container settings.** Two keys, both `None` by default. They apply to every container of the same kind (every Wooden Chest shares one setting).
  - `Exclude Container Key`: hold a modifier and press it to switch bulk transfer off for that kind of container. Press it again to switch it back on.
  - `Remember Amount Key`: hold Shift or Ctrl (not both) and press it to make that kind of container always use the preset. Press it again with the same amount to forget it.
- **The overlay says what moved.** If fewer cards moved than you asked for, the notice reads `Moved 6 of 12`. After a whole-stack batch it reads `Moved 37`. A batch that moved everything keeps its start notice.
- **A batch keeps its destination.** If you close the container, open the character window or open another container mid-batch, the batch stops with `Batch stopped: the destination changed`. A new modifier+click replaces a running batch. A full container ends it after three refused cards.
- **Pinned slots.** The faint placeholder a pinned slot keeps is never moved or counted. A modifier+right-click on something the game's own right-click ignores, such as a placed structure, does nothing. The card whose own popup is open still starts a batch.
- **Localized overlay.** On-screen text follows the game's language in English and Chinese. Config descriptions stay English.

### With no container open

The batch goes the way the game's own right-click goes:
- An Item-row pile goes to the board.
- A board pile comes into your Item row.
- With the character window open, Ctrl+Shift on a pile of rings moves only as many as the equipment slots take. Opening the window mid-batch stops the batch.
- In a trade, the offer is the destination (see Community Mod Chest below).

### Tag Filter notes

- Tags come from `Tag Filter Tags`, in priority order. The first listed tag the clicked card carries decides the batch. Keep the list narrow: a tag most cards carry sweeps unrelated items along.
- To stock Herbs and Fungi's Apothecary Shelf, set `Tag Filter Tags` to `tag_Herb,tag_HumanFood,tag_FoodRaw`, with `tag_Herb` first. Ctrl+Caps Lock+right-click on one herb then moves every herb you carry into the shelf and leaves other food. Tried in game with Chamomile and Yarrow only.
- The notice shows the tag's own in-game name, which is not always the setting minus `tag_`. The game calls `tag_Plateable` "Edible", so read the notice rather than guessing.
- Vanilla's `tag_Fuel` is a poor filter. It is on twigs, bark, straw, pine cones, arrows and wood carvings, but not on firewood, logs or charcoal.

## Installation

1. Install CSFFModFramework in `BepInEx/plugins/CSFF_Mod_Framework/`.
2. Download the latest release.
3. Copy the `Quick_Transfer` folder into `BepInEx/plugins/`.
4. Launch the game.

```
BepInEx/
  plugins/
    CSFF_Mod_Framework/
      CSFFModFramework.dll
    Quick_Transfer/
      Quick_Transfer.dll
      ModInfo.json
      Localization/
        SimpEn.csv
        SimpCn.csv
```

Optional: [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) (BepInEx 5 build) lets you change settings in game with F1. Look for "Quick_Transfer". Every setting applies at once, no restart.

## Configuration

The config file is `BepInEx/config/crispywhips.quick_transfer.cfg`, created on first launch.

**Transfer Settings**

| Setting | Default | Description |
|---------|---------|-------------|
| Transfer Amount | 5 | Cards moved when presets are off or a non-Ctrl/Shift modifier is used (1 to 9999) |
| Full Stack Mode | false | Every modifier+right-click moves the whole stack |
| Transfer Mouse Button | Right | Button that triggers a bulk transfer: `Right` or `Middle` |
| Consolidate Batch Sound | true | One sound per batch instead of one per card |
| Drain Matching Stacks | false | Carry on with the same item from the container's other slots |
| Widen For Non-Piling Cards | true | The same for cards that cannot pile, even with Drain Matching Stacks off |
| Prioritize Most Spoiled | false | Take the stack closest to spoiling first |

**Tag Filter**

| Setting | Default | Description |
|---------|---------|-------------|
| Tag Filter Key | CapsLock | Hold with the modifier+click to widen the batch by tag; `None` disables it |
| Tag Filter Tags | tag_HumanFood,tag_FoodRaw | Comma-separated tags, first match wins; empty disables the filter |

**Spoiled Only**

| Setting | Default | Description |
|---------|---------|-------------|
| Spoiled Only Key | LeftAlt | Hold with the modifier+click to move only cards past the threshold; `None` disables it. Ignored if it equals Modifier Key |
| Spoiled Only Threshold | 50 | Percent of the way to spoiling a card must be to move (1 to 100). A starting value: tune it to taste |

**Modifier Presets**

| Setting | Default | Description |
|---------|---------|-------------|
| Enable Modifier Presets | true | Turns the Shift, Ctrl and Ctrl+Shift combos on |
| Shift Preset Amount | 5 | Cards per Shift+right-click (1 to 9999) |
| Ctrl Preset Amount | 10 | Cards per Ctrl+right-click (1 to 9999) |
| Ctrl+Shift Preset Mode | All | `All`, `Half` (half the clicked stack, rounded up) or `AllButOne` |

**Keybindings**

| Setting | Default | Description |
|---------|---------|-------------|
| Modifier Key | LeftControl | Fallback modifier, used when presets are off |
| Increase Amount Key | Equals (=) | Raise the active amount: Ctrl for the Ctrl preset, Alt (or Shift without Repeat Action) for the Shift preset |
| Decrease Amount Key | Minus (-) | Lower the active amount, with the same keys |
| Reset Presets Key | Backspace | Hold the modifier and press it to reset amounts to 5 / 10 / 5; `None` disables it |
| Transfer Hotkey | X | Hold a modifier and press it over a card for a batch without clicking; `None` disables it |
| Undo Key | Z | Hold a modifier and press it to move the last batch back; `None` disables it |
| Yield Count Keys To Repeat Action | true | With Repeat Action installed, ignore its count keys while one of its modifier keys is held. `false` brings back the old behaviour, where one press changed both mods. No effect without Repeat Action |

**Containers**

| Setting | Default | Description |
|---------|---------|-------------|
| Exclude Container Key | None | Hold a modifier and press it to switch bulk transfer off or on for the open container's kind |
| Excluded Containers | (empty) | Container IDs with bulk transfer off. Normally filled by the key |
| Remember Amount Key | None | Hold Shift or Ctrl and press it to make the open container's kind always use that preset |
| Container Amounts | (empty) | Remembered amounts, such as `uniqueID/Shift=5;uniqueID/Ctrl=all`. Normally filled by the key |

## Compatibility

- Needs only CSFFModFramework. Without it the mod still loads, but its transfer patch is not applied and the overlay stays in built-in English.
- It does not change game data, only input handling.
- **Repeat Action:** both mods read Plus/Minus with Shift held, Repeat Action for its repeat count and Quick Transfer for its Shift preset. Quick Transfer steps aside. While one of Repeat Action's modifier keys is held (Left and Right Shift by default), it ignores Repeat Action's count keys (Equals, Keypad Plus, Minus, Keypad Minus by default), so only the repeat count changes. Use Alt+Plus/Minus for the Shift preset. Ctrl+Shift+Plus/Minus is not yielded.
  - With Repeat Action 2.2.1 or newer, Quick Transfer follows your rebinding of the count keys, and a key set to `None` is not yielded. With an older Repeat Action it uses the four defaults. If you rebind those keys, update both mods together.
  - Quick Transfer's on-screen hint still shows whenever Shift is held. Version 1.8.1 and earlier did not step aside, so one Shift+Plus press changed both numbers.
- **Utility Belt:** a batch never moves a card you locked in Utility Belt (its `L` key). Without Utility Belt, nothing changes. Utility Belt also bundles this same plugin, with the same plugin ID and config file. Install Quick Transfer on its own or through Utility Belt, not both. If both are present, BepInEx loads the newer copy and logs a harmless "Skipping ... because a newer version exists" warning for the other.
- **Community Mod Chest trades:** the same modifier+click works in the Inn Keeper's Trade window. Ctrl+right-click on a stack in your Item row put exactly 10 into the offer, and Ctrl+right-click on the offer moved them back. Opening or closing the window stops a running batch. Only filling and emptying the offer was tried, with Metal Nuggets. No trade was completed.
- **Water-Driven Infrastructure:** the Workshop is plain storage with action buttons, so a batch never starts one of its actions. Its storage keeps one card per slot. A push works as usual, and with `Widen For Non-Piling Cards` a pull takes the full count from neighbouring slots, so Ctrl+right-click on one nugget brings out ten.
- **Campfires and other fires:** fuel stays out of a batch. With a Campfire open, Ctrl+right-click on 10 Dry Pine Needles moved none of them, and the same click on 10 Raw Meat put 6 into the fire. Use the fire's Feed actions to burn fuel in bulk, or Repeat Action to repeat them. Tried on the Campfire only.
- **Cooking Pot:** its contents are ordinary cards in its popup, so the same modifier+click takes cards out of it. With the Ctrl preset at 3 and 5 Raw Meat inside, Ctrl+right-click took out three. Only the vanilla Cooking Pot was tried. Containers that list their contents as rows with their own Take button are not covered.
- **For mod authors:** `Quick_Transfer.Patcher.QuickTransferPatch.TryClickMoveOne(object card)` sends one card the game's own right-click and returns whether it moved. It never starts a batch. It refuses cards the game will not let you move by hand, pinned placeholders, cards locked in Utility Belt and cards in a closed container, and sets `QuickTransferPatch.LastRefusal`. Utility Belt's Auto-Pull Materials calls it by reflection. Quick Transfer declares no dependency on either mod. To read Repeat Action's keys, Quick Transfer looks up six `Keybindings` entries in its config: `Repeat Modifier Key`, `Repeat Modifier Key (Alt)`, `Count Up Key`, `Count Up Key (Alt)`, `Count Down Key` and `Count Down Key (Alt)`.

## Version history

Full notes are in [CHANGELOG.md](CHANGELOG.md).

- **1.9.3:** a broken translation now shows in the log as a warning.
- **1.9.2:** a card you locked with Utility Belt stays put when it is the card you modifier+click or press the Transfer Hotkey over.
- **1.9.1:** version number only.
- **1.9.0:** Tag Filter, Spoiled Only, Undo, Transfer Hotkey, per-container settings, All But One, Repeat Action key sharing, and batch safety fixes.
- **1.8.1:** extra warnings in the log, no gameplay change.

## Credits

Created by Jared (crispywhips). Built with [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX).
