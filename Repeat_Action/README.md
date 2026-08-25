# Repeat Action

**Version:** 2.0.2
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (EA 0.66)

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
- **Travel Support**: Repeat a direction action (North/South/East/West) to keep moving — the mod finds the matching direction on each new location card and stops cleanly when there's no path forward
- **Drag-Drop Support**: Repeat drag-drop actions like making twine, soaking reeds, or chopping trees with tools
- **Stack Support**: Repeat stack-button actions on card piles
- **Event-Aware**: With `Stop On Low Stats` enabled, the run stops when a critical stat condition starts blocking actions (dehydration, starvation, etc.); other popups pause the run until you dismiss them
- **Honest Stop Reasons**: When a repeat stops early, the notification shows the game's own reason (e.g. a missing requirement), not a guess

## Installation

### Requirements
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Card Survival: Fantasy Forest

Repeat Action is fully standalone as of 2.0.0 — it no longer uses CSFFModFramework.

### Steps
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
# Stop repeating when the game signals a critical condition (health, hunger, or event popup)
Stop On Low Stats = true

# Stop repeating when a drag-drop tool transforms (e.g. axe wears out and changes state)
Stop On Tool Break = true

# Per-stat stop floors (0 = disabled); stop repeating when stat drops below this % of max
Stamina Stop Threshold (%) = 0
Satiation Stop Threshold (%) = 0
Hydration Stop Threshold (%) = 0

[Timeout Settings]
# Maximum seconds to wait for the game to be ready before aborting the repeat sequence
Action Completion Timeout (seconds) = 30
```

## Compatibility

- **Quick Transfer**: This mod uses `Shift` as its modifier key, while Quick Transfer uses `Ctrl`, so they work together without conflict.
- **Other Mods**: Should be compatible with most mods. If you experience issues, please report them.
- **Dependencies:** None — fully standalone. No dependency on the framework or any content mod, and no other mod depends on Repeat Action.

## Supported Actions

**All player-initiated actions are captured by default.** The mod records any action you click in a card's popup or via drag-and-drop, then replays it. **A repeat succeeds only while the card and game conditions still match the original action's requirements** — if an action can no longer execute (card consumed, location depleted, conditions no longer met), the repeat stops cleanly and the on-screen notification tells you why. Stopping early is expected behavior, not a bug.

Examples of actions that work: Forage, Clear, Eat, Drink, Cook, Boil, Roast, Fry, Bake, Smoke, Dry, Make, Build, Plant, Harvest, Mine, Chop, Craft, Grind, Dig, Butcher, Skin, Tan, Sew, Smith, Repair, Fill, Pour, Brew, Ferment, Wash, Feed, Water, Till, Train, and any mod-added action.

### How replay works (2.0.0)

Each iteration, the mod re-finds your target card (by its card type, so a consumed-and-respawned target of the same kind still counts), re-finds the action on that live card, runs **the game's own availability check** (the same one that greys out buttons), and then dispatches through the game's own action pipeline — exactly as if you had clicked the button again. The iteration counts as done when the game finishes the action.

| Action Category | Behavior |
|-----------------|-----------|
| **Eat / Drink** | Stops when the item or container is used up and no identical one is available |
| **Travel (N/S/E/W)** | Finds the matching direction action on each new location card; stops when there's no path forward |
| **Drag-drop (Twine, Wash, etc.)** | Re-finds given and receiving cards each iteration; stops when either is gone |
| **Rest / Relax** | Never overwrites your primary action — resting between chops keeps `Shift+R` on the chop |

### Not Supported

| Action | Reason |
|--------|--------|
| **Continue** (event popup) | One-shot event outcome; auto-advancing through events would be harmful |

> **Tip:** All other actions captured through the card popup or drag-and-drop will be repeatable. If an action stops early, check the on-screen notification for the reason.

## Troubleshooting

**Q: The action doesn't repeat**
- Make sure you're pressing `Shift+R` (not just `R`)
- The action must be a player-initiated action (not a passive/automatic one)
- A notification saying "'ActionName' is not supported" means the action is not replayable — either it matched the mod's short blocklist (currently just the event-popup "Continue" button) or it was a non-repeatable one-off (e.g. discard, finish-game)
- Check the config file to verify keybinds

**Q: Repeat stops early / an action "doesn't actually repeat"**

The mod never forces an action through — each iteration re-runs the game's own requirement checks. Stopping early is expected whenever the card or game state no longer matches the original action:
- The target card was consumed or destroyed (eating the last of an item, burning the last fuel)
- The location or resource is depleted (Forage/Dig/Clear on an exhausted spot)
- A required stat dropped too low (check "Stop On Low Stats" and the per-stat threshold settings)
- The card no longer meets the action's conditions (field not grown, container empty, wrong card state)
- A one-shot action reached its limit (Skin, Pick Up hide, etc. — these can only happen once per card)
- For drag-drop, if the source card is used up the mod stops with "source used up"
- For travel, if there's no path in that direction the mod stops with "no more targets"

In all of these cases the repeat stops cleanly — the on-screen notification shows the specific reason, using the game's own requirement message where available (e.g. a missing stat or tool).

**Q: Repeat stops with a stamina/requirement message**
- The mod does not auto-rest for you (2.0.0 removed the automatic rest-between-iterations). Rest manually, then press `Shift+R` again — resting does not overwrite your last repeated action.

**Q: Mod not loading**
- Verify BepInEx is installed correctly
- Check `BepInEx/LogOutput.log` for errors

## Credits

Created by crispywhips

## License

MIT License - Feel free to modify and redistribute.
