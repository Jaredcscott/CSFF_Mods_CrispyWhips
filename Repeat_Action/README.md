# Repeat Action

**Version:** 2.2.3
**Author:** Jared (crispywhips)
**For:** Card Survival: Fantasy Forest (Early Access)

Press `Shift+R` to repeat your last action as many times as you set, with safety stops so a long batch never runs you into trouble. Each release is built against the game build current at the time; `CHANGELOG.md` names it.

**Requires:** BepInEx 5.x only. No other mod is needed.
**Languages:** English, plus Simplified Chinese notifications when the game is in Chinese.

## Install

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Card Survival: Fantasy Forest.
2. Extract this mod into `BepInEx/plugins/`.
3. Launch the game.

## Quick start

1. Do any action once: Forage, Chop, Eat, Craft, a drag-and-drop.
2. Press `Shift+R` to repeat it 5 times.
3. Raise or lower the count first with `Shift+Plus` and `Shift+Minus`, or `Shift+Mouse Wheel`. Hold a key to change it fast.
4. Press `Shift+R` again at any time to cancel.

Either Shift key works. Lower the count below 1 and it becomes **Unlimited**: the run goes until a stop condition ends it or you cancel. A backstop of 500 iterations (`Maximum Unlimited Iterations`) keeps a never-failing action from looping forever.

## Keys

| Keys | What it does |
|------|--------------|
| `Shift+R` | Repeat the last action, or cancel a run |
| `Shift+Plus`, `Shift+Minus`, `Shift+Mouse Wheel` | Raise or lower the count (keypad `+` and `-` work too) |
| `Shift+P` | Pin the action `Shift+R` would repeat now |
| `Shift+F` | Repeat the pinned action (does not cancel a run) |
| `Shift+G` | Pause a run after its current iteration; press again to resume |
| `Shift+K` | Start or stop recording a sequence |
| `Shift+M` | Play the chosen sequence |
| `Shift+U` | Choose the next recorded sequence |
| `Shift+N` | Load the next preset (does nothing during a run) |

All keys are settings. The count keys are `Count Up Key`, `Count Up Key (Alt)`, `Count Down Key` and `Count Down Key (Alt)`; `None` disables one. If you rebind a count key, update Quick Transfer too (see Compatibility).

## What it repeats

Any action you click in a card's popup or do by drag-and-drop: Forage, Chop, Eat, Cook, Craft, Plant, Harvest, Mine, Pour, Feed, Train and so on, including actions added by mods. A repeat only runs while the card and the game still allow the action, exactly as if you clicked the button again. When it can no longer run, the run stops and the notification gives the game's own reason. Stopping early is normal.

- **Travel:** repeats a direction (North, South, East, West) and finds it on each new location card. It stops when there is no path.
- **Drag-and-drop:** repeats actions like making twine or chopping with a tool. A repeat takes the dragged item from the container the last one came from, then from anywhere in your location.
- **Stacks and groups:** group actions such as Eat All repeat as one whole-group sweep per iteration. Turn on `Per-Card Group Repeat` to do one card of the group per iteration, so the count is a hard cap and the run stops with "no more targets" when the group is used up.
- **Rest and Relax** never replace the action you are repeating, so resting between chops keeps `Shift+R` on the chop. `Extra Rest-Like Actions` adds more names (for example `eat, drink`).
- **Not repeated:** Continue on event popups, anything in `Extra Blocked Actions`, and blueprint Build. Build is not captured, so it can be neither repeated nor recorded.

## Pin, pause, record, presets

- **Pinned action.** `Shift+P` pins the action `Shift+R` would repeat. `Shift+F` repeats it later with the current count, even after other actions or travel. One pin at a time. It lasts until you pin another, it turns out to be gone when you fire it, or you quit.
- **Pause.** `Shift+G` pauses after the current iteration. While paused you can eat, rest or craft by hand, and the run keeps its remaining count and its action. Time spent paused does not count toward `Stop After In-Game Hours`. `Shift+R` cancels a paused run.
- **Recorded sequences.** Press `Shift+K`, do a few actions ("plant, water, harvest"), then press `Shift+K` again. `Shift+M` plays the steps in order, repeated by the current count. A step that cannot run stops the play and names it ("step 2 of 3 (Water): ..."); no step is skipped. Your 10 most recent recordings are kept when you quit, except ones containing a Rest, a liquid pour, or an action the card does not list as its own button, which last for the session ("kept for this session"). A saved drag-and-drop step takes the first item of its kind it finds. Loading a save while recording drops the recording. You cannot record and run at the same time. Not yet confirmed in a running game.
- **Repeat Presets.** Write named sets of settings in `Repeat Presets` and `Shift+N` loads the next one. Format: `name: key=value, key=value; name: key=value`.

```
Repeat Presets = Chop: count=20, Stop At Nightfall=true; Snack: count=3, Lock To Original Target=true
```

  `count` sets the repeat count (0 is Unlimited). Any other key is the name of a true/false, whole-number or decimal setting outside `Keybindings`, held to that setting's own limits. List settings (`Extra Stat Thresholds`, `Extra Blocked Actions`, `Extra Rest-Like Actions`, `Stop At Quantity`) cannot be set by a preset. A preset overwrites values you set by hand. A pair that cannot be applied is skipped and noted in the log.

## Stop conditions

All of these end a run cleanly. Everything below is off unless noted.

| Setting | Stops the run when |
|---------|--------------------|
| `Stop On Low Stats` (on) | A status such as starving, dehydrated or exhausted blocks actions. The notification gives the game's own message |
| `Stamina`, `Satiation`, `Hydration Stop Threshold (%)` | That stat falls below your percentage |
| `Extra Stat Thresholds` | Any stat, vanilla or modded, crosses a floor or ceiling (see below) |
| `Stop On Tool Break` (on) | The tool you drag breaks: its usage bar runs out and it turns into another card or disappears. A dragged card the action itself uses up, such as food, never counts. Cards whose bar counts uses (soap, rennet) count when the last use runs out |
| `Use Spare Tool On Break` | Changes the above: the run carries on with another tool of the same kind in your location (carried, in a container here, or on the board). With none, it stops |
| `Stop Below Tool Durability (%)` | The dragged tool has less than that share of its full durability left. At 20, a knife with 2 of 10 uses left is used and one with 1 left stops the run |
| `Stop On Inventory Full` | Every container you carry is full. Ignored if you carry none. Off by default because many actions drop output on the ground |
| `Stop At Nightfall` | Dusk falls (17:00, or 19:00 in summer). Only a run that has been in daylight stops, so one started at night runs on |
| `Stop At Season Change` | The season changes during the run |
| `Stop After Encounter` | An encounter interrupted the run. The run stops before its next iteration |
| `Stop After In-Game Hours` (0 = off) | The run has used that much game time. It counts whole 15-minute ticks and judges the next iteration by the longest so far, so a run with uneven iterations can stop one early |
| `Stop At Quantity` | Your location holds enough of a card (see below) |

Other stop reasons you may see: the target card was used up, the spot is depleted, a requirement is no longer met, "locked target is gone", "game ended" (you loaded a save or went to the menu), or the Unlimited backstop. If you cannot tell why a run stopped, turn on `Verbose Run Diagnostics`; `BepInEx/LogOutput.log` then records every decision a run makes, only while a run is active.

An encounter that your repeated action starts holds the run until you have dealt with it, however long that takes, then the run carries on. The mod never answers or skips an encounter for you. An encounter already open between two iterations does not hold the run.

## Run control

- **Lock To Original Target:** a run stays on the exact card you clicked and stops with "locked target is gone" when it is used up. Normally it moves to another card of the same kind. It never locks the item you drag onto the card. A travel repeat stops after one move while this is on.
- **Iteration Delay (seconds):** up to 30 real seconds between iterations, never after the last. Cancelling still works during it.
- **Refuel From Pinned Action:** when a stat floor would stop a run, the run uses your pinned action once (say, Eat on a food you pinned) and carries on if the stat is back above its floor. The refuel is not counted as an iteration, and only the pinned action is ever used. Without a card of the pinned kind, the run stops with "(pinned action unavailable)"; if the stat is still low afterwards, "(refuel did not help)". `Maximum Refuels Per Run` (3, from 1 to 20) caps it. It never answers a ceiling, and a rest cannot be pinned, so it does not recover Stamina. A pinned stack or group action may use up the whole stack or group. Both defaults are a first guess and may change.
- **Remember Last Count:** keeps the count you last set across game restarts instead of returning to `Default Repeat Count`. To go back, turn the setting off.
- **Extra Blocked Actions:** comma-separated action names the mod never captures.

### Extra stat thresholds

`Extra Stat Thresholds` takes comma-separated entries written with a stat's UniqueID. The percent is a share of the stat's current maximum.

- `id:percent` (1 to 100) is a floor: stop when the stat falls below it.
- `id:>percent` (1 to 99) is a ceiling: stop when the stat rises above it, for stats where high is the danger. A ceiling of 100 or more is skipped.

Entries are checked in order after the fixed floors, and the first one crossed ends the run, naming the stat. A stat already past its limit stops the run before the first iteration.

```ini
Extra Stat Thresholds = 888d2d2a99e3f044291c6748a0fa8d78:30, 3b79a4c6d7e151044a1c56fbbd401d78:>60, 547f09c7ea6d11d4bbf6714255b0dfd5:>70
```

| Stat | UniqueID | Notes |
|------|----------|-------|
| Body Temperature | `888d2d2a99e3f044291c6748a0fa8d78` | floor |
| Morale | `4a27fb5da9326b545a5ef73f2b80316e` | floor |
| Stamina | `1cfd30cf13b69b949a0ac521f55a59a2` | same as the fixed floor |
| Satiation | `930cf914322e9f145af1315d96f85a28` | same as the fixed floor |
| Hydration | `95ca7c21ffad5e647acc3d9cb5bfcde6` | same as the fixed floor |
| Stress | `3b79a4c6d7e151044a1c56fbbd401d78` | ceiling; maximum 240, so `:>60` stops past 144 |
| Nausea | `547f09c7ea6d11d4bbf6714255b0dfd5` | ceiling; maximum 24, so `:>70` stops at 17 or more. Vomiting starts at 20, so keep a ceiling well below that |

Values are from the game's data for EA 0.68b. For a modded stat, copy the `UniqueID` from that mod's `GameStat/*.json`. A malformed entry is skipped without affecting the others; a UniqueID matching no stat never fires.

### Stop at quantity

`Stop At Quantity` takes comma-separated `id:count` entries (count 1 or more). The run stops once your location holds at least that many of the card, and the notification names it ("Rye Bread Slice reached 10"). With several entries, the first one reached ends the run.

It counts every card of that kind on the board where you are and in what you carry, including inside a carried bag, each card of a pile once. Cards in other locations do not count. It checks before every iteration and after each one, so a run that already has enough does not start. It counts cards, not liquid amounts. Pair it with Unlimited to repeat until you have enough.

```ini
Stop At Quantity = a1620f9713805d941b515c2b60a04169:10, b8885e78517570b4293c573871625e11:20
```

To find a card's UniqueID, open its `CardData/**/*.json` (for a modded card, in that mod's folder) and copy the `"UniqueID"` value. Vanilla examples for EA 0.68b:

| Card | UniqueID |
|------|----------|
| Fibers | `b8885e78517570b4293c573871625e11` |
| Stone | `a7384e5147b23a642809451cc4ef24fb` |
| Long Stick | `3db4c94184af274409f0d3eb16870f64` |
| Twigs | `fa8834c3111a71f47bdd23d66b33cf61` |
| Firewood | `758be01db25eb4649a288d64b7600b9b` |
| Wooden Plank | `57460207bbf77fa4fb6720aed5d84851` |
| Rope | `a7a58aa687df66e47a42fc13e0fdbeaa` |
| Flint | `bef002cfd45b3e8459864746f403cf73` |
| Clay | `68c14d265ea6c874ba79444d2e1ef7b3` |
| Charcoal | `51c751469bd15ce46804e399479d1764` |
| Clay Bowl | `a968f3eaffc6b9743b82982b5af2ab8c` |
| Fresh Leaves | `cf466b58520dcd047b7c3db67293c316` |
| Rye Bread Slice | `a1620f9713805d941b515c2b60a04169` |
| Wheat Bread Slice | `1cc70e04beedcc14c9308c0122d4e011` |

## Feedback and display

- **Notifications** show the kind of action being repeated and its progress. They name actions and cards in your game language. `Show Notifications` turns them off.
- **Progress bar** under the notification tracks the run (a moving sweep in Unlimited mode). `Show Progress Bar = false` hides it.
- **Count indicator** (off; `Show Count Indicator`): a small label in the top-right corner while a card popup is open. It shows the count ("Repeat: x5"), the action `Shift+R` would repeat ("Next: Eat", or "Next: none"), "Pinned: ..." while an action is pinned, "Paused" while paused, and the preset name after `Shift+N`. It moves below the popup's close button if it would cover it.
- **Completion sound** (off; `Play Completion Sound`): a short game sound when a run that did at least one iteration ends on its own. Not played when you cancel.
- **Chinese notifications:** with the game in Simplified Chinese, every notification is in Chinese. The translation has not been reviewed by a native speaker, and the font check was done on Windows only. Reasons the game itself gives appear in the game's own words.
- **Typing-safe:** while a text box has the cursor (renaming a card, Edit Description, blueprint or guide search), this mod's keys and the mouse wheel are ignored. Whether they work again right after you confirm with Enter is not yet confirmed in a running game.

## Settings

Edit `BepInEx/config/crispywhips.repeat_action.cfg`. Every number is limited to the range in its comment, and a value outside it is moved to the nearest end when the game starts. A `Default Repeat Count` above `Maximum Repeat Count` stays in the file, but the count starts at the maximum.

Or change them in game with the optional [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) (BepInEx 5 build): press F1 and find "Repeat_Action". A change made there applies at once, except `Default Repeat Count` and `Remember Last Count`, which set the count the game starts with.

| Setting | Default |
|---------|---------|
| `Default Repeat Count` (0 to 10000; 0 = Unlimited) | 5 |
| `Maximum Repeat Count` (1 to 10000) | 50 |
| `Maximum Unlimited Iterations` (1 to 10000) | 500 |
| `Action Completion Timeout (seconds)` (1 to 300) | 30 |
| `Iteration Delay (seconds)` (0 to 30) | 0 |
| `Stop After In-Game Hours` (0 to 240) | 0 |
| `Stamina`, `Satiation`, `Hydration Stop Threshold (%)` (0 to 100) | 0 |
| `Stop Below Tool Durability (%)` (0 to 99) | 0 |
| `Maximum Refuels Per Run` (1 to 20) | 3 |
| `Stop On Low Stats`, `Stop On Tool Break`, `Show Notifications`, `Show Progress Bar` | on |
| Every other true/false setting above | off |
| `Last Count` and `Saved Sequences` | managed by the mod, do not edit |

## Compatibility

- **Quick Transfer.** Version 1.9.0 and later steps aside from this mod's keys, so `Shift+Plus` and `Shift+Minus` change only the repeat count. Quick Transfer's own Shift preset is then nudged with `Alt+Plus` and `Alt+Minus`. With `Ctrl+Shift+Plus` or `Minus`, Quick Transfer changes nothing while this mod still counts. Quick Transfer's `Yield Count Keys To Repeat Action` (on by default) controls this, and 1.9.0 follows rebound count keys. Version 1.8.1 and earlier also react to Shift+Plus and Shift+Minus, so one press changes both mods' settings: update Quick Transfer, or turn off its `Enable Modifier Presets`. Quick Transfer's on-screen amount hint shows whenever Shift is held; it is only a hint.
- **Utility Belt.** A run never picks up a card you locked there (its `L` key) as a target, ingredient or tool. The card you used for the first repeat stays eligible. Utility Belt's `RepeatAction` options work with this mod: `Auto-Pull Materials` fetches a card the game wants in your hand and retries the repeat once, `Store Products In` moves what a run makes into a container you picked, and `Equip Spare Tool On Break` puts on a spare when a worn tool breaks. Without a worn-tool spare, a run whose worn tool breaks stops with "requirements no longer met". Repeat Action does not depend on Utility Belt.
- **Other mods:** should work with most. Please report problems.

## Troubleshooting

- **Nothing repeats.** Press `Shift+R`, not `R`. The action must be one you did yourself, not an automatic one. "'Name' is not supported" means the action is blocked (Continue, your own blocklist) or a one-off such as discard.
- **A run stops early.** Read the notification: it gives the reason. Common ones are a used-up card, a depleted spot, a stat below its floor, a broken or worn tool, a full inventory, or a stop condition you turned on. Turn on `Verbose Run Diagnostics` for details.
- **A run stops on Stamina.** The mod does not rest for you. Rest by hand, then press `Shift+R` again; resting does not replace the repeated action.
- **The mod does not load.** Check that BepInEx is installed correctly and read `BepInEx/LogOutput.log`.

## For mod authors

Repeat Action raises public events other mods can subscribe to. With no subscriber they are never raised and nothing changes.

- `Repeat_Action.Patcher.ActionPatch.OnRequirementsUnmet` (`Action<RequirementsUnmetArgs>`): raised when a repeat is refused for want of a card (a drag-drop whose dragged card is used up, or the game's availability check refusing the action). The args carry the action, the cards it acts on, and the cards and tags the game reports missing. A subscriber that moved a card into reach sets `RetryRequested`; the run waits for the game to settle and retries that dispatch once, without raising the event again.
- `Repeat_Action.Patcher.ActionPatch.OnIterationStarting` (`Action<int,int,string>`: run serial, iteration, run label) and `OnIterationDone` (`Action<int,int,bool,string>`: serial, iteration, whether it was the iteration's last step, label): raised around each step's own dispatch.

## Recent versions

Full history in `CHANGELOG.md`.

- **2.2.3:** a repeated pour no longer takes water from a container you locked with Utility Belt.
- **2.2.2:** version number only.
- **2.2.1:** repeat presets, recorded sequences, bindable count keys, Chinese notifications, Refuel From Pinned Action, spare-tool and pour fixes.
- **2.2.0:** pinned action, pause, and the Nightfall, Season Change, Encounter, In-Game Hours, Quantity and Tool Durability stops.

## Credits

Created by crispywhips.

## License

MIT License. Feel free to modify and redistribute.
