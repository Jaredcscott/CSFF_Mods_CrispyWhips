# Repeat Action — Changelog

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
