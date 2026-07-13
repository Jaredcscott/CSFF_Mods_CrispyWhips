# Repeat Action — Changelog

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
