# Repeat Action

**Version:** 1.6.4
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.65)

A quality-of-life mod that lets you automatically repeat your last action multiple times with a single keypress.

## Features

- **Repeat Last Action**: Press `Shift+R` to repeat your most recent action (Forage, Clear, Chop, Travel, etc.)
- **Dual Keybindings**: Two fully configurable modifier+key pairs — `LeftShift+R` and `RightShift+R` by default — so you can use either hand
- **Adjustable Count**: Press `Shift+Plus(= key)` or `Shift+Minus` to increase/decrease how many times to repeat (1-50). **Hold** either key for rapid adjustment. Either Shift key works.
- **Visual Feedback**: On-screen notifications show what action is being repeated and progress
- **Safety Stop**: Automatically stops if health or hunger drops critically low (configurable)
- **Stat Thresholds**: Configurable per-stat stop conditions — set a Satiation, Hydration, or Stamina % floor and the repeat halts before you hit critical levels
- **Tool-Break Stop**: Stops automatically when a drag-drop tool transforms (e.g. axe wears out mid-run)
- **Cancel Anytime**: Press `Shift+R` again while repeating to cancel
- **Smart Travel**: Rests before each travel step to ensure stamina, then moves in the chosen direction. Stops automatically when there's no path forward, a critical stat event triggers, or you cancel. Each step: Rest → Travel → repeat.
- **Smart Chopping**: Automatically rests between chop/cut/fell iterations to recover stamina and allow small trees to respawn
- **Drag-Drop Support**: Repeat drag-drop actions like making twine, soaking reeds, or chopping trees with tools
- **Pick Up Support**: Repeat picking up soaking reeds, flax stems, and nettle stems
- **Event-Aware**: Stops the repeat sequence if a game event popup (dehydration, starvation, encounters, etc.) triggers mid-run

## Installation

### Requirements
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Card Survival: Fantasy Forest
- **CSFFModFramework** (recommended) — Repeat Action's card-identification helper calls the framework's `Api.CardUtil`/`Api.Reflect` utilities. The mod declares `CSFFModFramework` as a `SoftDependency` for load order; without it installed, that helper throws and action-repeat replay can fail.

### Steps
1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Card Survival: Fantasy Forest
2. Install CSFFModFramework in `BepInEx/plugins/CSFF_Mod_Framework/`
3. Download this mod and extract to your `BepInEx/plugins/` folder
4. Launch the game

## Usage

1. Perform any action in-game (Forage, Clear, Collect, etc.)
2. Press `Shift+R` to repeat that action 5 times by default
3. Use `Shift+Plus` or `Shift+Minus` to adjust repeat count before triggering
4. Press `Shift+R` again to cancel mid-repeat

### Default Keybinds

| Key Combination | Action |
|----------------|--------|
| `LeftShift+R` or `RightShift+R` | Repeat last action / Cancel repeat |
| `Shift+Plus` (`Shift+=`) | Increase repeat count |
| `Shift+Minus` | Decrease repeat count |

Both Shift keys work for all controls. The two keybinding pairs are independently configurable.

## Configuration

Edit `BepInEx/config/crispywhips.repeat_action.cfg` to customize:

```ini
[Keybindings]
# Primary keybinding (left-hand usage)
Repeat Action Key = R
Repeat Modifier Key = LeftShift

# Alternate keybinding (right-hand usage)
Repeat Action Key (Alt) = R
Repeat Modifier Key (Alt) = RightShift

[Repeat Settings]
# Default number of times to repeat an action
Default Repeat Count = 5

# Maximum number of times an action can be repeated
Maximum Repeat Count = 50

[Display]
# Show on-screen notifications when repeating actions
Show Notifications = true

[Safety]
# Stop repeating when the game signals a critical condition (health, hunger, or event popup)
Stop On Low Stats = true

# Stop repeating when a drag-drop tool transforms (e.g. axe wears out and changes state)
Stop On Tool Break = true

# Per-stat stop floors (0 = disabled); stop repeating when stat drops below this % of max
Stamina Stop Threshold (%) = 0
Satiation Stop Threshold (%) = 0
Hydration Stop Threshold (%) = 0

[Timeout Settings]
# Maximum seconds to wait for an action to complete before aborting the repeat sequence
Action Completion Timeout (seconds) = 30

# Frames to wait for ActionRoutine to fire after a button click (60 frames ≈ 1 second at 60fps)
Gate 1 Timeout (frames) = 60

# Maximum seconds to wait for a rest action to complete before travel
Pre-Travel Rest Timeout (seconds) = 15
```

## Compatibility

- **Quick Transfer**: This mod uses `Shift` as its modifier key, while Quick Transfer uses `Ctrl`, so they work together without conflict.
- **Other Mods**: Should be compatible with most mods. If you experience issues, please report them.
- **Dependencies:** `CSFFModFramework` (soft — see Requirements above). No dependency on any content mod, and no other mod depends on Repeat Action.

## Supported Actions

**All player-initiated actions are supported by default.** The mod captures any action you click in a card's popup or via drag-and-drop, then replays it. If an action can no longer execute (card consumed, conditions not met), it stops cleanly.

Examples of actions that work: Forage, Clear, Eat, Drink, Cook, Boil, Roast, Fry, Bake, Smoke, Dry, Make, Build, Plant, Harvest, Mine, Chop, Craft, Grind, Dig, Butcher, Skin, Tan, Sew, Smith, Repair, Fill, Pour, Brew, Ferment, Wash, Feed, Water, Till, Train, and any mod-added action.

### Special Behaviors

| Action Category | Special Handling |
|-----------------|-----------------|
| **Eat** | Popup-based replay; stops when card is consumed and no more of the same item exist |
| **Drink** | Stops when the container runs dry |
| **Chop / Cut / Fell** | Automatically rests between iterations to recover stamina; waits for trees to respawn |
| **Travel (N/S/E/W)** | Rests before each step for stamina; stops when no path forward or a stat event triggers |
| **Drag-drop (Twine, Wash, etc.)** | Refreshes given and receiving card each iteration; stops when either is exhausted |

### Not Supported

| Action | Reason |
|--------|--------|
| **Continue** (event popup) | One-shot event outcome; auto-advancing through events would be harmful |

> **Tip:** All other actions captured through the card popup or drag-and-drop will be repeatable. If an action stops early, check the on-screen notification for the reason.

## Troubleshooting

**Q: The action doesn't repeat**
- Make sure you're pressing `Shift+R` (not just `R`)
- The action must be a player-initiated action (not a passive/automatic one)
- A notification saying "'ActionName' is not supported" means it matched the mod's short blocklist (currently just the event-popup "Continue" button)
- Check the config file to verify keybinds

**Q: Repeat stops unexpectedly**
- If "Stop On Low Stats" is enabled, it may have stopped due to low health/hunger
- Check if the target card still exists (some actions consume the card)
- For drag-drop, if the tool breaks the mod will stop with "source exhausted"
- For travel, if there's no path in that direction the mod stops with "Can't go [direction]"

**Q: Chopping trees is slow**
- The mod rests between chops to recover stamina and allow small tree respawns — this is intentional
- Each chop-rest cycle takes roughly one game tick

**Q: Travel is slow**
- The mod rests before each travel step to ensure you have stamina — this is intentional
- The rest also checks for stat events (dehydration, starvation) and stops if one triggers

**Q: Mod not loading**
- Verify BepInEx is installed correctly
- Check `BepInEx/LogOutput.log` for errors

## Credits

Created by crispywhips

## License

MIT License - Feel free to modify and redistribute.
