# Changelog

## 1.0.5

### Changed - log verbosity (pre-distribution pass)
- **Both `[Diagnostics]` config options now default to OFF** (`Enable Diagnostics`,
  `Enable Pouch Transfer Diagnostics`). They shipped ON in 1.0.4 to gather evidence for four
  still-unconfirmed Partner bugs, but measured against a real 2-boot `LogOutput.log` they were
  the single largest source of mod log output in the whole suite: 47 Info lines, of which the
  `FindCardToEquip` equip trace alone accounted for 33. The diagnostics themselves are unchanged
  and every one of their Info sites already honours the gate, so reproducing any of those four
  bugs is still one config flip away. **If you are reporting one of those bugs, turn the matching
  option back on and re-capture the log.**
- Note for existing installs: BepInEx keeps the value already written in
  `BepInEx/config/crispywhips.partner_overhaul.cfg`, so this new default only applies to fresh
  installs. Existing users who want the quieter log must set both options to `false` themselves.
- `[GameLoadPatch]` per-target duty attachment is now one aggregate Info line
  (`attached N duty ref(s) to vanilla actions`) instead of 11 individual lines. The per-target
  list is still available at Debug level.
- `[WoodReserveListPatch]`'s startup config echo now logs at Info only when the reserved list is
  NON-empty; the empty case (the shipped default, so the case every player hits) drops to Debug.
  This partially reverses 1.0.4's decision to always echo the count: that rationale
  (distinguishing "confirmed no-op by design" from "config never loaded") is preserved and is
  still observable by enabling Debug logging.
- `[WoundStacking]`'s per-suppressed-wound line drops to Debug; it fired once per suppressed
  wound during combat.

## 1.0.4

### Diagnostics -- no behavior change
- `WoodReserveListPatch` now emits one `[WoodReserveListPatch]` LogInfo line on its first
  evaluation each run stating the reserved fuel/wood UID count, even when it is zero (e.g.
  `Reserved fuel/wood UIDs: 0 (list empty; the reserve filter is a no-op)`). This is a second
  startup Info line beyond the mod's single `PartnerOverhaul vX loaded.` summary -- previously
  `RefreshReservedSetIfNeeded` only logged when the reserved set was non-empty, so a default
  (empty) config produced zero log output for this feature, indistinguishable from the config
  never having loaded at all. Config-echo diagnostic of the same class as the framework's own
  `LocalizationLoader: language=` line (see root CLAUDE.md's Debugging Discipline). Filter
  behavior (`FilterReservedItems`) is unchanged; only added Info logging.

## 1.0.3

### Fixed
- A Partner's Energy level is now visible on their inspection card. Vanilla's `Partner_Energy`
  NPCStat has fully-authored status text ("Looks exhausted." / "Looks very tired." / "Looks
  tired." / "Looks full of energy.") but ships every entry with `VisibleToPlayer`/`ShowInMainTab`
  false, unlike the sibling need stats (Satiation/BodyTemperature/Thirst/Wakefulness), which show
  theirs. `Patcher/GameLoadPatch.cs` `FixPartnerEnergyVisibility` flips both flags on each entry
  at boot (`GameLoad.LoadMainGameData` postfix), matching the pattern already used by their
  siblings — no new text authored, nothing else about the stat's behavior changes.

## 1.0.2

### Diagnostics (opt-in, default ON) — no behavior change
- Added `[PouchTransferDiagnostics]` LogInfo logging on the generic engine liquid-transfer clamp
  (`InGameCardBase.ClampLiquidTransferQuantity`), scoped to fire only when Pouch or AcornFlourRaw
  is one of the two cards involved, for the reported pouch/acorn-flour stacking math bug. New
  independent config toggle: "Enable Pouch Transfer Diagnostics" (`Patcher/PouchTransferDiagnosticsPatch.cs`).
  Diagnostic only — the underlying stacking bug is not fixed by this release.

## 1.0.1

### Added (opt-in, default OFF)
- "Consolidate Duplicate Wounds" config option — when enabled, a combat round will not apply a
  wound the player already has equipped (`Patcher/WoundStackingPatch.cs`, transpiler on
  `EncounterPopup.GenerateAndApplyPlayerWound`). Filters only the per-round wound-candidate list;
  damage resolution, stat changes, armor durability and the encounter log are unchanged.
  **Not yet verified in-game.**

## 1.0.0 — Initial release

### Fixed
- Partners can now eat stew/broth (Eat/EatFree duty wired onto `OLD_LQ_StewWater`/`LQ_NightBroth`
  Drink actions; thirst still targets plain water only).
- Partners resume cleaning finished Cabin/MudHut/Enclosure/MudHutEnclosure/MudHutExpansionLeft
  homes (Clean duty wired onto the finished-home "Clean" action, matching the mid-construction
  variants' existing wiring).
- Partners stop endlessly re-watering an already-saturated garden plot (armed a Hydration-percent
  gate on GardenPlot's "Water" action).
- Partners can feed Pine Needles/Charcoal into a Fireplace (Firekeeping duty wired onto both
  actions).
- Removed a stray "Defecate" button from the "Tree Top" scenic event.
- NPC action sounds (eating, working) are no longer audible from a different environment than the
  player's current one.
- A Partner's dialog no longer interrupts an in-progress player action (e.g. a spirit-summon
  ritual).

### Added (opt-in, default OFF)
- "Reduce Partner Move Costs" config option — discounts inter-environment travel move-cost.
- "Reserved Fuel/Wood UIDs" config option — exclude specific items from Partner auto-fuel-feeding.

### Diagnostics (opt-in, default ON)
- LogInfo-level diagnostics for 4 still-unconfirmed bugs: fishing-line AI freeze, brain-tanning
  stuck-on-first-hide, clothes/carry-weight/temperature, cauldron ownership reset.
