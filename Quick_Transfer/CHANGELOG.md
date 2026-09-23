# Quick Transfer — Changelog

All notable changes to this mod are documented here.

## [1.8.1] - 2026-09-23

### Fixed
- **Silent skips on the transfer click now leave one warning each.** If the clicked card, its model, its UniqueID or its slot cannot be read (the shape a game update renaming a field takes), Quick Transfer used to skip the transfer with nothing in the log. Each cause now logs one warning per session. Found by the 2026-09-15 code-quality review (G5).
- **A translation with broken placeholders is now diagnosable.** When a localized overlay string's `{0}`-style placeholders do not match the English template, the overlay already fell back to English; it now also leaves a debug breadcrumb naming the key. Found by the 2026-09-15 audit (preflight D17).

Diagnostics only: no gameplay change.

## [1.8.0] - 2026-09-07

### Added
- **Configurable trigger button** - new `Transfer Mouse Button` setting (`Right`, default, or `Middle`). On `Middle`, modifier+right-click goes back to being the game's ordinary one-card quick-move while bulk transfers move to middle-click. Because vanilla's `InGameCardBase.OnPointerClick` only reaches `SwapCard` on a right-click, a middle-click batch now moves the full requested count rather than count-1 - the trigger click itself moves nothing. Left-click is deliberately not offered: the game routes it to card inspection, and the resulting inspected card fails `SwapCard`'s own `InspectedCard != this` guard, so a left trigger would open a popup and transfer nothing.
- **Reset presets hotkey** - hold the modifier and press `Reset Presets Key` (default Backspace, set to `None` to disable) to restore Shift/Ctrl/custom amounts to 5 / 10 / 5, confirmed by the on-screen overlay. Backspace was picked because the game's own code reads no key by that name.
- **Combo label on the live indicator** - the persistent hint now reads `Quick Transfer: 10 (Ctrl)` instead of `Quick Transfer: 10`, matching the suffix the adjustment keys already showed. The suffix appears only when a preset combo is what produced the number, so it stays absent in Full Stack Mode and Legacy Custom Mode rather than labelling a number the combo didn't decide.
- **Half preset for Ctrl+Shift** (added 2026-09-08, before 1.8.0 shipped) - new `Ctrl+Shift Preset Mode` setting (`All`, default, or `Half`). On `Half`, Ctrl+Shift+click moves half of the clicked stack, rounded up (8 -> 4, 7 -> 4, 1 -> 1), filling the gap between the Ctrl preset and All. The count is read off the clicked slot's pile inside the click prefix, before vanilla moves the first card, because the overlay has no slot in hand - so while the combo is held it reads `Quick Transfer: Half (Ctrl+Shift)` rather than a number. If the pile count cannot be read, the Ctrl preset is used instead.
- **Drain Matching Stacks** (added 2026-09-08, before 1.8.0 shipped) - new `Drain Matching Stacks` setting, off by default. When on, a bulk transfer that empties the clicked slot continues with the same item from the other slots of the same container (the same open inventory, or the same board area), up to the requested count; the clicked slot always drains first. Two slots count as the same container when the game built them from the same `DynamicViewLayoutGroup` with the same slot type; if that cannot be resolved for either slot, the old same-slot rule stands. The `CannotBeTransferred` and location-card exclusions apply to sibling slots exactly as before. To make this work, a transfer's progress is now measured on the slot the moved card actually sits in - which is the clicked slot itself whenever the setting is off, so nothing changes there.
- **Localized overlay** (added 2026-09-08, before 1.8.0 shipped) - the on-screen text (`Quick Transfer: 10 (Ctrl)`, `All`, `Half`, the combo suffixes, and `Presets reset: 5 / 10 / 5`) now comes from the mod's first `Localization/` folder (`SimpEn.csv` / `SimpCn.csv`), resolved through the game's own `LocalizationManager.GetText` for whatever language is active, so it follows a language switch and never forces one. Every string keeps a built-in English default for when the framework or a translation is absent. Config descriptions are BepInEx-owned and stay English.

### Changed
- **One card sound per batch instead of one per card** - `GraphicsManager.MoveCardToSlot` ends by playing the moved card's appearance sound, and `RandomSoundPlay` pools a new `AudioSource` per call with no throttling, so a bulk transfer fired one sound per card on consecutive frames. Those are now muted for the cards this mod moves and a single sound plays when the batch ends. A card with no `WhenCreatedSounds` stays silent exactly as before. Disable with `Consolidate Batch Sound = false`. The suppressing prefix is gated on the mod's own in-flight flag, so every sound outside a Quick Transfer batch is untouched.

## [1.7.7] — 2026-08-16

### Internal
- Version-sync bump only (`ModInfo.json`, `Plugin.cs`, `README.md`, rebuilt `bin/Release/Quick_Transfer.dll`) as part of a batch release-process commit. No `Patcher/` source or behavior changes since 1.7.6 — subsequent commits touching this mod before this entry was written were `lib/Assembly-CSharp.dll` refreshes for game-version updates and `.audit/` report regeneration, neither of which change shipped behavior.

## [1.7.6] — 2026-08-09

### Documentation
- Publish-pass review: verified README.md and ModInfo.json feature descriptions against shipped C# source (modifier presets, adjustable presets, live indicator, Full Stack Mode, Legacy Custom Mode, and the `CannotBeTransferred` guard in `QuickTransferPatch.IsValidCandidate`) — no discrepancies found, no content changes needed. Version bump only.

## [1.7.5] — 2026-08-04

### Fixed
- `OnPointerClick` method-resolution failure (e.g. a future game update renaming/re-signaturing `CardGraphics.OnPointerClick`) now logs an error instead of silently skipping the Harmony patch. Previously the mod would print its "loaded." line, apply nothing, and go completely inert with zero trace in the log — this closes that diagnostic gap (`Patcher/QuickTransferPatch.cs`).

## [1.7.4] — 2026-07-23

### Fixed
- Bulk-transfer coroutine no longer stalls silently against a refused destination (e.g. a full/over-weight inventory). Previously it counted every re-invoked right-click as a successful transfer regardless of whether the card actually moved, so a rejected move made it grind through the entire remaining count (up to ~9,998 no-op iterations in Full Stack Mode) while repeatedly re-triggering the destination's "cannot carry" feedback. The coroutine now compares the source slot's pile count before and after each invoke and stops after 3 consecutive no-progress attempts, same as it already did for an empty source slot.

## [1.7.2] — 2026-07-12

### Changed

- Reflection for the game's card-click handler lookup now uses CSFFModFramework `Api.Reflect` — the local per-(Type, name) member cache is replaced by the framework's shared utility. CSFFModFramework is now declared a `SoftDependency`; the mod still loads without it, but the transfer patch won't apply if the type lookup fails.
- `Reflect.TryGetMember` replaces the multi-path field/property resolution fallback chain, preserving the same null-skip semantics (try each candidate name; fall through only when the value is null).

---

## [1.7.0] — 2026-06-21

### Added
- **Full Stack Mode** — a new config option under "Transfer Settings". When enabled, any modifier+right-click always transfers the entire stack, ignoring count adjustment keys and preset amounts.

### Changed
- Transfer count maximum raised from 1,000 to 9,999 for all three preset slots (Shift, Ctrl, default) and their in-game adjustment keys. Lets Full Stack Mode's sentinel value (9,999) display correctly as "All" in the notification.
- Count adjustment keys (modifier+Plus/Minus) are now suppressed while Full Stack Mode is active, so they don't interfere with stack-all behavior.

### Fixed
- Stack-of-identical-items transfer no longer does a full scene scan on every frame of the coroutine. The coroutine now caches the first matching `CardGraphics` object and only re-scans when that object leaves the source slot, reducing per-frame work for large stacks.
- `CardType 8` cards (construction/explorable location cards) are now excluded from candidate matching, preventing accidental transfer of environment cards that share an inventory slot.
- Slot-lookup fallback chain (`CurrentSlot` → `ContainerSlot` → `ParentSlot` → `CardLogic.SlotOwner`) is now shared between the prefix capture and the coroutine candidate check, so both paths use identical resolution logic.
