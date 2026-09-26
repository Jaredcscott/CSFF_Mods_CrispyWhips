# Repeat Action — Changelog

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

## [2.0.2] — 2026-08-16

### Changed

- **Version-sync / game-data refresh bump.** No source changes to `ActionPatch.cs` or `Plugin.cs` — `lib/Assembly-CSharp.dll` was refreshed against the EA 0.66h game update (per CLAUDE.md's Game-Update Reference Refresh rule; RepeatAction compiles directly against the plain assembly) and the mod rebuilt clean. No player-visible behavior change.

## [2.0.1] — 2026-08-08

### Fixed

- **EA 0.66bb compatibility.** Repeat stopped working after the game update — the game's `CardAction.CollectActionModifiers` gained an `InGameNPC` parameter, so the mod's availability check threw `MissingMethodException` on every dispatch. Rebuilt against the 0.66bb game assembly and updated the call. No behavior change otherwise.

## [2.0.0] — 2026-07-20

### Changed

- **Complete replay-engine rewrite (native dispatch).** The mod no longer replays actions by simulating popup button clicks and guessing whether they worked. It now captures your click at the game's own dispatch layer (`GameManager.PerformAction` and its stack / drag-drop / group variants) and replays by calling that same layer directly: re-find the target card, re-find the action on the live card, run the game's own availability check, dispatch, and wait for the game to finish the action. This removes the three root causes of "repeat almost never works":
  - Button-index replay clicking the wrong (or no) button when the popup layout shifted.
  - The "no effect" validator that killed valid iterations of any action that doesn't advance the game clock or destroy its card (most crafting/production actions).
  - Requirement checks running against stale action state, producing spurious "Action unavailable" stops.
- Stop reasons now surface the game's own blocked-action message (e.g. missing stat/tool) instead of a heuristic guess, and every stop reason is logged at Info level.
- ~3,700 lines of heuristics replaced by ~600 lines of typed code compiled against the current game assembly (EA 0.65h).

### Removed

- **Automatic rest-between-iterations** (Smart Travel pre-rest, Smart Chopping rest). If a repeat stops for stamina, rest manually and press `Shift+R` — rest/relax still never overwrites your captured primary action.
- **CSFFModFramework dependency** — the mod is now fully standalone.
- Obsolete config entries: `Gate 1 Timeout (frames)`, `Pre-Travel Rest Timeout (seconds)` (harmless if still present in your cfg).

### Fixed

- Repeat now works for the broad class of actions that produce output without consuming their card (craft, process, harvest-style actions) — previously these stopped after one iteration with "Stopped - no effect".
- Stat-threshold safety stops (Stamina/Satiation/Hydration floors) now actually work — 1.x read runtime stat values from a location where they don't exist, so thresholds silently never triggered.
- Travel repeat re-finds the direction action on the new location card each step instead of relying on the old location's popup.

## [1.6.3] — 2026-07-12

### Changed

- Reflection for game type lookup now uses CSFFModFramework `Api.Reflect` — the local per-(Type, name) member cache is replaced by the framework's shared reflection utility. CSFFModFramework is now declared a `SoftDependency`; the mod still loads without it.

### Fixed

- **Harvest-style field actions** (DismantleActions with a `CollectionName`, e.g. seasonal field harvests in Community Mod Chest) now correctly bypass stale-action validation. These actions fire through `PerformGroupInventoryAction` at execution time but click via `OnButtonClicked`, causing their captured action object to fail the stale-validation check even when the action is valid. A new `lastCapturedViaGroupAction` flag covers this case so the validation skip applies correctly to Forage, Clear, _and_ any Harvest-style action.

---

## [1.6.2] — 2026-07-10

### Fixed

- **Thresh (and similar transform-type actions)** — improved handling when the card's model is briefly unavailable after a transform action; these cases now correctly count as consumed/transformed rather than "no effect". Clearer notification when all stackable bundles have been processed.
- Upgraded key diagnostic logs for transform detection from Debug (invisible by default) to Info, making it easier to diagnose action-capture issues via `BepInEx/LogOutput.log`.

## [1.6.0] — 2026-06-21

### Changed

- **All player-initiated actions are now supported by default.** The mod previously required each action to match a keyword from a maintained allowlist — only ~70 specific keywords were captured. It now uses a structural gate to identify player actions and captures everything except a tiny blocklist. Cooking, building, planting, fishing, animal care, and any mod-added action now work without needing to be individually whitelisted.
- The only blocked action is **Continue** on event popups — auto-advancing through story events would cause unrecoverable side effects.

### Added

- **Stop On Tool Break** safety option — repeat sequence stops automatically when a drag-drop tool transforms (e.g. an axe wears out and changes card state mid-batch).
- **Per-stat stop thresholds** — configurable stop floors (0–100%) for Stamina, Satiation, and Hydration. Repeat halts early if any enabled stat drops below its threshold.
- **Timeout settings** — configure how long the mod waits for an action to complete (`Action Completion Timeout`, default 30s) and how many frames to wait for an action to register after a button click (`Gate 1 Timeout`, default 60 frames).

### Fixed

- **EA 0.65 compatibility** — updated for the current game version. Prior release targeted EA 0.63.
- Many actions that were silently skipped due to missing allowlist keywords now work: Cook, Boil, Roast, Fry, Bake, Smoke, Dry, Build, Plant, Collect, Care (animal husbandry), Disassemble, Rip, Deconstruct, and all mod-added actions (H&F herb dosing, WDI fishing/smelting, ACT forge actions, etc.).
