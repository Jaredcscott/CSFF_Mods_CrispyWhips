# Repeat Action

**Version:** 1.3.2

A quality-of-life mod that lets you automatically repeat your last action multiple times with a single keypress.

## Features

- **Repeat Last Action**: Press `Shift+R` to repeat your most recent action (Forage, Clear, Chop, Travel, etc.)
- **Dual Keybindings**: Two fully configurable modifier+key pairs — `LeftShift+R` and `RightShift+R` by default — so you can use either hand
- **Adjustable Count**: Press `Shift+Plus(= key)` or `Shift+Minus` to increase/decrease how many times to repeat (1-50). **Hold** either key for rapid adjustment. Either Shift key works.
- **Visual Feedback**: On-screen notifications show what action is being repeated and progress
- **Safety Stop**: Automatically stops if health or hunger drops critically low (configurable)
- **Cancel Anytime**: Press `Shift+R` again while repeating to cancel
- **Smart Travel**: Rests before each travel step to ensure stamina, then moves in the chosen direction. Stops automatically when there's no path forward, a critical stat event triggers, or you cancel. Each step: Rest → Travel → repeat.
- **Smart Chopping**: Automatically rests between chop/cut/fell iterations to recover stamina and allow small trees to respawn
- **Drag-Drop Support**: Repeat drag-drop actions like making twine, soaking reeds, or chopping trees with tools
- **Pick Up Support**: Repeat picking up soaking reeds, flax stems, and nettle stems
- **Event-Aware**: Pauses if a game event popup appears during repetition

## Installation

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Card Survival: Fantasy Forest
2. Download this mod and extract to your `BepInEx/plugins/` folder
3. Launch the game

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
# Automatically stop repeating when the game signals a critical action blocker
# (event popups, critical stat events such as dehydration or starvation)
Stop On Low Stats = true
```

## Compatibility

- **Quick Transfer**: This mod uses `Shift` as its modifier key, while Quick Transfer uses `Ctrl`, so they work together without conflict.
- **Other Mods**: Should be compatible with most mods. If you experience issues, please report them.

## Supported Actions

Actions are matched by keyword — any in-game action whose name contains a supported keyword will be captured and repeatable.

### Supported Action Table

| Category | In-Game Actions | Type | Notes |
|----------|----------------|------|-------|
| **Location Gathering** | Forage, Clear | Group action (location cards) | Click on Clearings, Groves, etc. |
| **Consumption** | Eat Raw, Eat Cooked, Drink | Popup button | Consumes the card; stops when supply runs out |
| **Woodcutting** | Chop Wood, Cut Tree, Fell a Tree | Drag-drop / Popup | Automatically rests between chops for stamina recovery; small trees respawn after rest |
| **Resource Extraction** | Extract Fibers, Harvest | Popup button | Reeds, nettles, crops, meadow grass |
| **Mining** | Mine | Popup button | Flint veins, witchstone veins |
| **Crafting (drag-drop)** | Make Twine, Soak Reeds, Craft Tourniquet | Drag card onto card | Drag one card onto another |
| **Crafting (popup)** | Make Clay, Grind, Dig | Popup button | Mud, clay, grinding actions |
| **Travel** | North, South, East, West | Popup button | Rest → Travel → Rest → Travel; stops when no path available or stat event triggers |
| **Pick Up** | Pick up (soaking reeds/flax/nettle) | Popup button | Location cards that transform back to items |
| **Rest & Recovery** | Meditate, Rest, Relax, Sharpen | Popup button | Cardless actions executed directly |

### Not Supported

These action types are intentionally excluded to prevent unintended side effects:

| Action Type | Reason |
|-------------|--------|
| Build, Dismantle | Multi-step construction; risky to auto-repeat |
| Cook, Boil, Roast, Fry, Bake, Smoke, Dry | Cooking on fire; complex card state |
| Fill, Pour, Empty | Liquid transfer; quantity tracking unreliable |
| Plant, Water, Fertilize | Farming setup; usually done once per card |
| Fish, Hunt, Butcher, Skin, Tan | Combat/encounter triggers; event popups |
| Place, Move | Inventory management; not a timed action |

> **Tip:** If you try to repeat an unsupported action, a notification will show the action name with "is not supported".

## Troubleshooting

**Q: The action doesn't repeat**
- Make sure you're pressing `Shift+R` (not just `R`)
- The action must be a player-initiated action (not a passive/automatic one)
- A notification saying "'ActionName' is not supported" means it's not in the allowed list
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
