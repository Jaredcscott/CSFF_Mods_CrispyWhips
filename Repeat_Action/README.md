# Repeat Action

**Version:** 2.1.5
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (Early Access)

> Each release is compiled against the game build current at the time - see `CHANGELOG.md` for the
> exact build a given version was built on.

A quality-of-life mod that lets you automatically repeat your last action multiple times with a single keypress.

## Features

- **Repeat Last Action**: Press `Shift+R` to repeat your most recent action (Forage, Clear, Chop, Travel, etc.)
- **Dual Keybindings**: Two fully configurable modifier+key pairs — `LeftShift+R` and `RightShift+R` by default — so you can use either hand
- **Adjustable Count**: Press `Shift+Plus(= key)` or `Shift+Minus` to increase/decrease how many times to repeat (1-50). **Hold** either key for rapid adjustment, or **scroll the mouse wheel** while holding Shift. Either Shift key works.
- **Unlimited Mode**: Lower the count below 1 and it becomes **Unlimited** - the repeat runs until a safety stop, a requirement failure, or you cancel. A configurable backstop (default 500 iterations) keeps a never-failing action from looping forever.
- **Visual Feedback**: On-screen notifications show which kind of action is being repeated (action / stack / drag-drop / group) and its progress, and a fill bar under the notification tracks the run from start to finish (a moving sweep in Unlimited mode). The bar vanishes when the run ends; turn it off with `Show Progress Bar = false`
- **Count Indicator** *(off by default)*: Turn on `Show Count Indicator` for a small persistent "Repeat: x5" label in the top-right corner whenever a card popup is open. It updates live as you change the count and hides when no popup is open
- **Safety Stop**: Automatically stops when one of your status conditions starts blocking actions (starving, dehydrated, exhausted), reporting the game's own message for it (configurable)
- **Stat Thresholds**: Configurable per-stat stop conditions — set a Satiation, Hydration, or Stamina % floor and the repeat halts before you hit critical levels. `Extra Stat Thresholds` extends this to **any** stat, vanilla or modded, as `guid:percent` pairs (see [Extra stat thresholds](#extra-stat-thresholds))
- **Tool-Break Stop**: Stops automatically when a drag-drop tool transforms (e.g. axe wears out mid-run)
- **Inventory-Full Stop** *(off by default)*: Stops once every container you are carrying is full. Off by default because many actions drop their output on the ground rather than into your inventory; it is also ignored entirely when you carry no container.
- **Custom Blocklist**: Add your own comma-separated action names to `Extra Blocked Actions` to keep the mod from ever capturing them
- **Cancel Anytime**: Press `Shift+R` again while repeating to cancel
- **Travel Support**: Repeat a direction action (North/South/East/West) to keep moving — the mod finds the matching direction on each new location card and stops cleanly when there's no path forward
- **Drag-Drop Support**: Repeat drag-drop actions like making twine, soaking reeds, or chopping trees with tools
- **Stack Support**: Repeat stack-button actions on card piles
- **Group Actions, Two Ways**: Group actions (Eat All, group harvests) replay as one whole-group sweep per iteration by default. Turn on `Per-Card Group Repeat` to process exactly one card of the captured group per iteration instead, so the count is a hard per-card cap and the run stops with "no more targets" when the group runs out
- **Event-Aware**: With `Stop On Low Stats` enabled, the run stops when a critical stat condition starts blocking actions (dehydration, starvation, etc.); other popups pause the run until you dismiss them
- **Honest Stop Reasons**: When a repeat stops early, the notification shows the game's own reason (e.g. a missing requirement), not a guess
- **Verbose Run Diagnostics** *(off by default)*: When on, `BepInEx/LogOutput.log` records every decision a run makes while it is active - which safety gate tripped and with what values, which card could not be found, what the game's availability check said - so you can read why a run stopped without enabling BepInEx debug logging. Never logs outside a run

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
| `Shift+Minus` | Decrease repeat count (below 1 = Unlimited) |
| `Shift+Mouse Wheel` | Increase / decrease repeat count |

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
# Default number of times to repeat an action (0 = Unlimited)
Default Repeat Count = 5

# Maximum number of times an action can be repeated
Maximum Repeat Count = 50

# Hard ceiling for Unlimited mode - a backstop, not the usual way a run ends
Maximum Unlimited Iterations = 500

# Group actions: false = one whole-group sweep per iteration (original behavior);
# true = exactly one card of the captured group per iteration
Per-Card Group Repeat = false

# Comma-separated action names to never repeat, merged with the built-in "continue"
Extra Blocked Actions =

# Log an Info-level line for every stop/abort decision while a run is active (off = summary lines only)
Verbose Run Diagnostics = false

[Display]
# Show on-screen notifications when repeating actions
Show Notifications = true

# Small persistent "Repeat: x5" label in the top-right corner while a card popup is open
Show Count Indicator = false

# Fill bar under the progress notification while a run is active (sweep in Unlimited mode)
Show Progress Bar = true

[Safety]
# Stop repeating when one of your status conditions blocks actions (starving, dehydrated, ...)
Stop On Low Stats = true

# Stop repeating when a drag-drop tool transforms (e.g. axe wears out and changes state)
Stop On Tool Break = true

# Stop once every container you are carrying is full (ignored if you carry no container)
Stop On Inventory Full = false

# Per-stat stop floors (0 = disabled); stop repeating when stat drops below this % of max
Stamina Stop Threshold (%) = 0
Satiation Stop Threshold (%) = 0
Hydration Stop Threshold (%) = 0

# Stop floors for ANY stat (vanilla or modded): comma-separated guid:percent pairs, empty = none
Extra Stat Thresholds =

[Timeout Settings]
# Maximum seconds to wait for the game to be ready before aborting the repeat sequence
Action Completion Timeout (seconds) = 30
```

### Extra stat thresholds

`Extra Stat Thresholds` takes comma-separated `guid:percent` pairs. `guid` is the stat's UniqueID and
`percent` (1-100) is the share of the stat's **current maximum** below which the run stops, exactly like
the three fixed floors above. Entries are checked in the order written, after the fixed floors, and the
first one crossed ends the run with the stat's own name in the notification ("Body Temperature below 30%").

```ini
# Stop when Body Temperature drops under 30% of its maximum, or Morale under 25%
Extra Stat Thresholds = 888d2d2a99e3f044291c6748a0fa8d78:30, 4a27fb5da9326b545a5ef73f2b80316e:25
```

| Vanilla stat | UniqueID |
|--------------|----------|
| Body Temperature | `888d2d2a99e3f044291c6748a0fa8d78` |
| Morale | `4a27fb5da9326b545a5ef73f2b80316e` |
| Stamina (same as the fixed floor) | `1cfd30cf13b69b949a0ac521f55a59a2` |
| Satiation (same as the fixed floor) | `930cf914322e9f145af1315d96f85a28` |
| Hydration (same as the fixed floor) | `95ca7c21ffad5e647acc3d9cb5bfcde6` |

For a **modded** stat, use the `UniqueID` from that mod's `GameStat/*.json` file. A malformed entry is
skipped without affecting the others, and a UniqueID that matches no stat simply never fires; turn on
`Verbose Run Diagnostics` to see either case reported in `BepInEx/LogOutput.log` during a run. Floors
suit stats that drain toward zero; a stat where *high* is the danger (e.g. Stress) has no "below" floor
that helps.

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
| Anything you list in `Extra Blocked Actions` | Your own additions to the blocklist, matched as whole words, case-insensitive |

> **Tip:** All other actions captured through the card popup or drag-and-drop will be repeatable. If an action stops early, check the on-screen notification for the reason.

## Troubleshooting

**Q: The action doesn't repeat**
- Make sure you're pressing `Shift+R` (not just `R`)
- The action must be a player-initiated action (not a passive/automatic one)
- A notification saying "'ActionName' is not supported" means the action is not replayable - either it matched the blocklist (the built-in event-popup "Continue", plus anything you added to `Extra Blocked Actions`) or it was a non-repeatable one-off (e.g. discard, finish-game)
- Check the config file to verify keybinds

**Q: Repeat stops early / an action "doesn't actually repeat"**

The mod never forces an action through — each iteration re-runs the game's own requirement checks. Stopping early is expected whenever the card or game state no longer matches the original action:
- The target card was consumed or destroyed (eating the last of an item, burning the last fuel)
- The location or resource is depleted (Forage/Dig/Clear on an exhausted spot)
- A required stat dropped too low (check "Stop On Low Stats", the per-stat threshold settings and `Extra Stat Thresholds`)
- The card no longer meets the action's conditions (field not grown, container empty, wrong card state)
- A one-shot action reached its limit (Skin, Pick Up hide, etc. — these can only happen once per card)
- For drag-drop, if the source card is used up the mod stops with "source used up"
- For travel, if there's no path in that direction the mod stops with "no more targets"
- With "Stop On Inventory Full" enabled, every container you carry is full ("inventory full")
- In Unlimited mode, the `Maximum Unlimited Iterations` backstop was reached

In all of these cases the repeat stops cleanly — the on-screen notification shows the specific reason, using the game's own requirement message where available (e.g. a missing stat or tool).

Still unsure why a run stopped? Turn on `Verbose Run Diagnostics` in the config and run it again: `BepInEx/LogOutput.log` will then name the exact gate, card lookup or availability check that ended the run, with the values it saw.

**Q: Repeat stops with a stamina/requirement message**
- The mod does not auto-rest for you (2.0.0 removed the automatic rest-between-iterations). Rest manually, then press `Shift+R` again — resting does not overwrite your last repeated action.

**Q: Mod not loading**
- Verify BepInEx is installed correctly
- Check `BepInEx/LogOutput.log` for errors

## Credits

Created by crispywhips

## License

MIT License - Feel free to modify and redistribute.
