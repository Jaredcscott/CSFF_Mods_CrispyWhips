# Quick Transfer — Changelog

All notable changes to this mod are documented here.

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
