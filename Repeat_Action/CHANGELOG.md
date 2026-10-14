# Repeat Action: Changelog

## [2.2.2] - 2026-10-12

Version number only. Everything new since the last Nexus release (2.1.6) is listed under 2.2.1 and 2.2.0 below.

## [2.2.1] - 2026-10-06

2.2.0 reached players inside Utility Belt 0.1.0, so everything built after that is listed here.

### Added

- **Repeat presets.** Write named sets of settings in `Repeat Presets` (for example `Chop: count=20, Stop At Nightfall=true; Snack: count=3, Lock To Original Target=true`) and press `Shift+N` (`Next Preset Key`) to load the next one. A preset can set `count` (0 is Unlimited) and any true/false, whole-number or decimal setting outside `Keybindings`, but not list settings. A preset overwrites values you set by hand. Empty by default.
- **Recorded sequences.** Press `Shift+K` (`Record Sequence Key`), do a few actions, press `Shift+K` again, then `Shift+M` (`Play Sequence Key`) plays them in order, repeated by the current count. A step that cannot run stops the play and names it; no step is skipped. `Shift+U` (`Next Sequence Key`) switches recording. Your 10 most recent recordings are kept in `Saved Sequences`, except ones with a Rest, a liquid pour or an action the card does not list as its own button, which last only for the session. Not yet confirmed by a person in a running game.
- **Bindable count keys.** `Count Up Key`, `Count Up Key (Alt)`, `Count Down Key` and `Count Down Key (Alt)` replace the four fixed keys (defaults unchanged; `None` disables one). Quick Transfer 1.9.0 follows rebound keys; with an older Quick Transfer, update both. Not yet confirmed in a running game.
- **Chinese notifications, and names in your game language.** With the game in Simplified Chinese, all notifications are in Chinese. In every language, notifications now name actions and cards the way the game shows them in that language. The Chinese has not been reviewed by a native speaker; corrections are welcome. The font check was done on Windows only.
- **Refuel From Pinned Action** (`Refuel From Pinned Action`, off; `Maximum Refuels Per Run`, 3). When a stat floor would stop a run, the run uses your pinned action once (such as Eat) and carries on if the stat is back over its floor. The refuel is not counted as an iteration. If no card of the pinned kind is left, or the stat is still low, the run stops and says why. It never answers a ceiling, a rest cannot be pinned (so it does not recover Stamina), and a pinned stack or group action may use up the whole stack or group. Both defaults are a first guess.
- **Utility Belt integration.** With Utility Belt installed, a run never picks a card you locked there. Utility Belt can also fetch a card the game wants in your hand and retry the repeat once (Auto-Pull Materials), and move what a run makes into a container you picked (Store Products In). Repeat Action has no dependency on Utility Belt.
- **Modder API.** Public events `Repeat_Action.Patcher.ActionPatch.OnRequirementsUnmet` (with `RetryRequested`), `OnIterationStarting` and `OnIterationDone` let other mods react to a repeat. Never raised without a subscriber.

### Changed

- **Build is no longer listed among repeatable actions.** Pressing Build on a blueprint (a structure or a station recipe such as the Herbs & Fungi Oil Press) was never captured, so it can be neither repeated nor recorded. The mod did not change; the docs and the `Saved Sequences` description said more than it did. The mod's description now mentions Pause and Resume and presets.

### Fixed

- **Stop On Tool Break and Use Spare Tool On Break now notice a tool breaking.** A worn-out dragged tool used to be missed, so the run just took the next tool or stopped with "source used up". A run now stops with "tool changed", or carries on with a spare if Use Spare Tool On Break is on. A dragged card the action itself uses up, such as food on a fire, is never treated as a broken tool.
- **The count indicator no longer covers the popup's close button.** It moves just below the button when it would.
- **The Repeat keys work again right after you confirm a rename with Enter.** They used to stay dead until you clicked elsewhere. Not yet confirmed in a running game.
- **A repeated pour keeps pouring into the container you chose and stops when the liquid can no longer move.** Before, a change in the target (wine freezing, say) made the run pour into another container, and it could pour a container into itself. It now stops with "liquid can no longer be transferred".

## [2.2.0] - 2026-09-28

Built against EA 0.68b. Every new setting is off (or 0) by default, so apart from `Shift+P` and `Shift+F` nothing changes unless you turn something on.

### Added

- **Pinned action (`Shift+P` to pin, `Shift+F` to fire).** Pin the action `Shift+R` would repeat, then repeat it later with the current count, even after other actions or travel. One pin at a time; it lasts until you pin another, it is gone when you fire it (the run stops with "pin cleared"), or you quit. `Shift+F` never cancels a run. Keys: `Pin Action Key`, `Fire Pinned Action Key`.
- **Pause and resume (`Shift+G`, `Pause Key`).** Pauses a run after its current iteration so you can eat, rest or craft by hand. The run keeps its remaining count and action. `Shift+R` cancels a paused run; loading a save ends it ("game ended"). Time spent paused does not count toward `Stop After In-Game Hours`.
- **`Stop At Nightfall`** stops a run at dusk (17:00, or 19:00 in summer). Only a run that has been in daylight stops.
- **`Stop At Season Change`** stops a run when the season changes.
- **`Stop After In-Game Hours`** (up to 240) ends a run after that much game time, so with Unlimited you can "chop for 3 hours". It counts whole 15-minute ticks and can stop one iteration early when iterations vary in length.
- **`Stop At Quantity`** ends a run once your location holds enough of a card, as `uniqueid:count` entries ("Rye Bread Slice reached 10").
- **`Stop After Encounter`** ends a run before its next iteration once an encounter interrupted it. The mod never answers an encounter for you.
- **`Stop Below Tool Durability (%)`** (up to 99) stops a drag-drop run before it uses a tool with less than that share of its durability left.
- **`Use Spare Tool On Break`** carries on with another tool of the same kind in your location when the dragged tool breaks, instead of stopping.
- **`Extra Stat Thresholds` accepts ceilings.** `id:>percent` stops the run when a stat rises above that share of its maximum, for stats where high is the danger (Stress, Nausea). Entries without `>` are floors as before.
- **`Extra Rest-Like Actions`** lists more actions that, like Rest and Relax, never replace the action `Shift+R` repeats (for example `eat, drink`).
- **`Lock To Original Target`** keeps a run on the exact card you clicked and stops with "locked target is gone" when it is used up.
- **`Iteration Delay (seconds)`** (up to 30) adds a real-time pause between iterations.
- **`Remember Last Count`** keeps the count you last set across game restarts.
- **`Play Completion Sound`** plays a short game sound when a run ends on its own.
- **The count indicator names the action.** With `Show Count Indicator` on, a second line shows "Next: <action>" (or "Next: none"), and a third shows "Pinned: <action>" while one is pinned.
- `Verbose Run Diagnostics` now also reports each Iteration Delay pause and what the Nightfall and Season Change stops saw.

### Fixed

- **Finishing an encounter no longer replaces the action `Shift+R` repeats.** Nothing is recorded while an encounter is in progress.
- **An Eat or Drink repeat no longer moves on to a different food** (a poisonous one included) once yours runs out. The same-name search now only follows location cards, which is what lets travel find North on each new location.
- **Typing in a text box no longer fires the Repeat keys.** A capital R, P or F or a plus sign typed into a card rename, Edit Description or a search box used to start, pin or fire a run or change the count. While a text box has the cursor, the mod ignores its keys and the mouse wheel.
- **A repeated drag-drop keeps drawing the dragged item from where the last one came from** (the bag or chest, or the board if it was loose), before looking elsewhere in your location.
- **A default count above the maximum no longer starts above it, and three settings now have limits.** `Default Repeat Count` is 0 to 10000, `Maximum Repeat Count` 1 to 10000 and `Action Completion Timeout (seconds)` 1 to 300. A value outside its range moves to the nearest end when the game starts.
- **Loading a save during a run now ends the run** ("game ended") instead of repeating the action in the loaded game. Your pinned action is kept.
- **Docs:** the README no longer claims Repeat Action and Quick Transfer never conflicted. Quick Transfer 1.8.1 and earlier also react to `Shift+Plus` and `Shift+Minus`; Quick Transfer 1.9.0 steps aside. Keypad `+` and `-` are now in the keys table. The Event-Aware text now says an encounter your action starts holds the run until you deal with it.

## [2.1.6] - 2026-09-25

Built against EA 0.68b.

### Fixed

- **Repeated group actions now count flavour, spices and time the way the game's own button does.** A repeat used to replay the exact action copies from your original click, so every iteration carried the flavour and spice totals of the cards that were in the group back then. With `Per-Card Group Repeat` on, each single card eaten applied the flavour total of the whole captured group and paid the whole group's time cost, and in a mixed group the cards after the first kind of food cost no time at all. With spiced food, the spice flavour grew stronger on every iteration in either mode. Each iteration now rebuilds the group from the cards actually there, exactly as pressing the button again would: every card's requirements are checked again (a card that no longer qualifies is left out, as the button leaves it out), and flavour, spices and time are counted from the cards being processed. If a game update ever breaks this, group actions report as not supported instead of repeating the old way.
- **A repeat can no longer act on a card that is not on your board.** After travel the game keeps some cards from other places loaded in the background (companions and other characters, cards at an ally's location), and a repeat could pick one of those as its target. Targets must now be in your current location. Items in the bags you carry still count, since they travel with you.
- **If the game's own availability check fails with an error, the run now stops and says so** ("availability check failed (see log)"), with the full error written to `BepInEx/LogOutput.log` as a warning (once per kind of error; repeats of it go to Verbose Run Diagnostics). Before, the repeat carried on without checking the action's requirements at all.
- **A game update that breaks one of the mod's hooks now disables only that kind of action**, with an error naming it, instead of also disabling every hook set up after it.
- **Verbose Run Diagnostics now explains a stat stop that can never fire.** A stop floor on a stat your run does not track, or on a stat whose maximum is 0, leaves a "this stop cannot fire" line instead of silently never triggering.

## [2.1.5] - 2026-09-08

Built against EA 0.67i.

### Added

- **`Extra Stat Thresholds` setting (empty by default).** Stop floors for ANY stat, vanilla or modded, alongside the three fixed Stamina / Satiation / Hydration floors. Syntax: comma-separated `guid:percent` pairs, where `guid` is the stat's UniqueID and `percent` (1-100) is the share of the stat's current maximum below which the run stops, e.g. `888d2d2a99e3f044291c6748a0fa8d78:30, 4a27fb5da9326b545a5ef73f2b80316e:25` stops when Body Temperature falls under 30% or Morale under 25%. The stop notification names the stat ("Body Temperature below 30%") using the stat's own in-game name, so a modded vital reads correctly. Entries are checked in the order written, after the three fixed floors, and the first one crossed stops the run. A malformed entry (no colon, non-numeric or out-of-range percent) is skipped with a log breadcrumb and never affects the other entries or the fixed floors; a UniqueID that resolves to no stat leaves the same "this stop cannot fire" breadcrumb the fixed floors already do. Both breadcrumbs surface at Info level when `Verbose Run Diagnostics` is on. Empty means no change in behavior.

## [2.1.4] - 2026-09-08

Built against EA 0.67i.

### Added

- **In-run progress bar (`Show Progress Bar`, on by default).** A fill bar now sits just under the "{completed}/{count}" notification for the whole run, filling as iterations complete and reaching full at completion; it stays up as long as the final "Complete" / "Stopped" notification, then vanishes. Unlike the notification, which fades two seconds after each iteration, the bar is visible throughout, so a long iteration still shows a run is in progress. Unlimited runs have no fixed total (the only denominator is the safety backstop, which is not what you are counting toward), so they show a moving sweep instead of a fill; the notification still carries the raw count. The bar is drawn only while a run is active, so it is unobtrusive by default; set `Show Progress Bar = false` to turn it off. It is independent of `Show Notifications`.

## [2.1.3] - 2026-09-08

Built against EA 0.67i.

### Added

- **`Show Count Indicator` setting (off by default).** When on, a small persistent "Repeat: x5" (or "Repeat: Unlimited") label sits in the top-right corner whenever a card popup is open, so you can read the configured count without starting a run. It updates live as you Shift+scroll or press Shift+Plus/Minus and disappears when no popup is open. It uses the game's own open-popup signal (the inspection popup the game currently has up), so it also shows over NPC, blueprint and container popups. Off means no visual change at all.

## [2.1.2] - 2026-09-07

Built against EA 0.67i.

### Added

- **`Verbose Run Diagnostics` setting (off by default).** When on, every stop/abort decision a run makes is written to `BepInEx/LogOutput.log` at Info level while the run is active: which safety gate tripped and with what values (e.g. "Stamina is 12/100 (12%), under its 20% floor"), which card could not be found on the board, what the game's own availability check said, and one line per dispatched iteration. It answers "why did my run stop after one iteration?" without enabling BepInEx debug logging. Off keeps the log exactly as before (the start and stop summary lines only), and the setting never logs outside a run, so a clean boot still shows exactly one Repeat Action line.

## [2.1.1] - 2026-09-07

Built against EA 0.67i.

### Added

- **`Per-Card Group Repeat` setting (off by default).** Group actions (Eat All, group harvests, ...) have always replayed as one whole-group sweep per iteration, so a count of 5 meant five sweeps. With this setting on, each iteration processes exactly ONE card of the captured group, in the order they were captured, so the count is a hard per-card cap; the run stops with "no more targets" once the group is exhausted, and the opening notification reads "group, per card" so you can tell which mode is active. Off keeps the original whole-group behavior unchanged - a player who never touches the setting sees no difference.

## [2.1.0] - 2026-09-05

Built against EA 0.67h.

### Added

- **Unlimited mode.** Lower the repeat count below 1 and it becomes "Unlimited" - the run continues until a safety stop, a requirement failure, or you cancel. A new `Maximum Unlimited Iterations` setting (default 500) is a backstop against an action that never fails, not the expected way a run ends; hitting it is reported as its own stop reason.
- **Mouse-wheel count adjustment.** Scroll while holding either Shift to raise or lower the repeat count, alongside the existing `Shift+Plus` / `Shift+Minus`.
- **`Stop On Inventory Full` safety stop (off by default).** Halts the run once every container you are carrying is full. It is off by default because many actions drop their output on the ground rather than into your inventory, and it is ignored entirely when you carry no container - so it can only end a run that genuinely has nowhere to put its output.
- **`Extra Blocked Actions` setting.** A comma-separated list of action names the mod will never capture, merged with the built-in "continue". Lets you exclude an action you never want to batch without a code change.

### Changed

- **Stop reasons name the real condition.** A run halted by `Stop On Low Stats` previously always reported "event triggered", even when the actual cause was a status blocker such as starvation or exhaustion. It now reports the game's own message for the blocker that stopped it, falling back to a neutral "blocked by your condition" only when the game supplies no text.
- **The repeat notification says which kind of action it is replaying** - action, stack, drag-drop, or group.
- **`Stop On Low Stats` description corrected.** Its tooltip (and the matching README line) claimed it also covered event popups. It never did: the toggle gates status blockers only, and popups are handled separately by the completion wait. Behavior is unchanged; only the description was wrong.
- **README no longer pins a game version** in its header - it drifted stale twice. The build a release was compiled against is recorded here in the changelog instead.

### Fixed

- **Liquid transfers now re-check `CannotBeTransferred` on every replayed iteration.** The vanilla flag is enforced on the drag path only, so a programmatic replay never consulted it (CLAUDE.md §Programmatic Card Movement). Defence in depth: the reachable paths were already covered by the drag-hover gate and the mod's UID re-resolution, so this is not a fix for an observed bug.
- **Diagnostics.** A per-stat stop whose vanilla stat GUID fails to resolve now leaves a debug breadcrumb instead of silently never firing, and a dispatch funnel that can no longer be found on `GameManager` is reported as an error at startup rather than surfacing later as a failed repeat.

## [2.0.2] - 2026-08-16

### Changed

- **Version-sync / game-data refresh bump.** No source changes to `ActionPatch.cs` or `Plugin.cs`, `lib/Assembly-CSharp.dll` was refreshed against the EA 0.66h game update (per CLAUDE.md's Game-Update Reference Refresh rule; RepeatAction compiles directly against the plain assembly) and the mod rebuilt clean. No player-visible behavior change.

## [2.0.1] - 2026-08-08

### Fixed

- **EA 0.66bb compatibility.** Repeat stopped working after the game update, the game's `CardAction.CollectActionModifiers` gained an `InGameNPC` parameter, so the mod's availability check threw `MissingMethodException` on every dispatch. Rebuilt against the 0.66bb game assembly and updated the call. No behavior change otherwise.

## [2.0.0] - 2026-07-20

### Changed

- **Complete replay-engine rewrite (native dispatch).** The mod no longer replays actions by simulating popup button clicks and guessing whether they worked. It now captures your click at the game's own dispatch layer (`GameManager.PerformAction` and its stack / drag-drop / group variants) and replays by calling that same layer directly: re-find the target card, re-find the action on the live card, run the game's own availability check, dispatch, and wait for the game to finish the action. This removes the three root causes of "repeat almost never works":
  - Button-index replay clicking the wrong (or no) button when the popup layout shifted.
  - The "no effect" validator that killed valid iterations of any action that doesn't advance the game clock or destroy its card (most crafting/production actions).
  - Requirement checks running against stale action state, producing spurious "Action unavailable" stops.
- Stop reasons now surface the game's own blocked-action message (e.g. missing stat/tool) instead of a heuristic guess, and every stop reason is logged at Info level.
- ~3,700 lines of heuristics replaced by ~600 lines of typed code compiled against the current game assembly (EA 0.65h).

### Removed

- **Automatic rest-between-iterations** (Smart Travel pre-rest, Smart Chopping rest). If a repeat stops for stamina, rest manually and press `Shift+R`, rest/relax still never overwrites your captured primary action.
- **CSFFModFramework dependency**, the mod is now fully standalone.
- Obsolete config entries: `Gate 1 Timeout (frames)`, `Pre-Travel Rest Timeout (seconds)` (harmless if still present in your cfg).

### Fixed

- Repeat now works for the broad class of actions that produce output without consuming their card (craft, process, harvest-style actions), previously these stopped after one iteration with "Stopped - no effect".
- Stat-threshold safety stops (Stamina/Satiation/Hydration floors) now actually work, 1.x read runtime stat values from a location where they don't exist, so thresholds silently never triggered.
- Travel repeat re-finds the direction action on the new location card each step instead of relying on the old location's popup.

## [1.6.3] - 2026-07-12

### Changed

- Reflection for game type lookup now uses CSFFModFramework `Api.Reflect`, the local per-(Type, name) member cache is replaced by the framework's shared reflection utility. CSFFModFramework is now declared a `SoftDependency`; the mod still loads without it.

### Fixed

- **Harvest-style field actions** (DismantleActions with a `CollectionName`, e.g. seasonal field harvests in Community Mod Chest) now correctly bypass stale-action validation. These actions fire through `PerformGroupInventoryAction` at execution time but click via `OnButtonClicked`, causing their captured action object to fail the stale-validation check even when the action is valid. A new `lastCapturedViaGroupAction` flag covers this case so the validation skip applies correctly to Forage, Clear, _and_ any Harvest-style action.

---

## [1.6.2] - 2026-07-10

### Fixed

- **Thresh (and similar transform-type actions)**, improved handling when the card's model is briefly unavailable after a transform action; these cases now correctly count as consumed/transformed rather than "no effect". Clearer notification when all stackable bundles have been processed.
- Upgraded key diagnostic logs for transform detection from Debug (invisible by default) to Info, making it easier to diagnose action-capture issues via `BepInEx/LogOutput.log`.

## [1.6.0] - 2026-06-21

### Changed

- **All player-initiated actions are now supported by default.** The mod previously required each action to match a keyword from a maintained allowlist, only ~70 specific keywords were captured. It now uses a structural gate to identify player actions and captures everything except a tiny blocklist. Cooking, building, planting, fishing, animal care, and any mod-added action now work without needing to be individually whitelisted.
- The only blocked action is **Continue** on event popups, auto-advancing through story events would cause unrecoverable side effects.

### Added

- **Stop On Tool Break** safety option, repeat sequence stops automatically when a drag-drop tool transforms (e.g. an axe wears out and changes card state mid-batch).
- **Per-stat stop thresholds**, configurable stop floors (0–100%) for Stamina, Satiation, and Hydration. Repeat halts early if any enabled stat drops below its threshold.
- **Timeout settings**, configure how long the mod waits for an action to complete (`Action Completion Timeout`, default 30s) and how many frames to wait for an action to register after a button click (`Gate 1 Timeout`, default 60 frames).

### Fixed

- **EA 0.65 compatibility**, updated for the current game version. Prior release targeted EA 0.63.
- Many actions that were silently skipped due to missing allowlist keywords now work: Cook, Boil, Roast, Fry, Bake, Smoke, Dry, Build, Plant, Collect, Care (animal husbandry), Disassemble, Rip, Deconstruct, and all mod-added actions (H&F herb dosing, WDI fishing/smelting, ACT forge actions, etc.).
