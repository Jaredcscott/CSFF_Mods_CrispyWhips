# Roadmap: QuickTransfer
Version at time of writing: 1.7.5
Date: 2026-08-04
Audit score: 9/10 — PASS (0 CRITICAL, 1 minor code-quality warning, deferred/optional)

## Current State

**Theme**: A quality-of-life input utility — bulk-transfers card stacks between inventories via modifier+right-click presets (Shift=5, Ctrl=10, Ctrl+Shift=all), with in-game adjustable amounts, a live count overlay, and a Full-Stack mode. Container-agnostic because it replays the game's own right-click handler rather than reimplementing the transfer path.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images (pure C# mod — 3 source files: `Plugin.cs`, `Patcher/QuickTransferPatch.cs`, `GlobalUsing.cs`).

**Stability**: 9/10 — 0 CRITICAL, 0 DESIGN GAP, 1 minor code-quality warning (bounded per-frame scene scan on large individual-card batches, deferred/optional). QT-1 in-game confirmed (2026-07-30); the silent patch-resolution log gap (W1) is fixed (2026-08-04).

**Open work**: None blocking. Retrospectives clean (0 🔴 / 0 🟡 affecting QuickTransfer); the one graduated retro — `companion-container-prevention` — is re-verified in current source (`CannotBeTransferred` guard present).

**Framework compliance**: Tier 2 (reflection). Uses the framework's `Reflect.TryGetType`/`Reflect.TryGetMember` service instead of a mod-local reflection cache. `ActionRouter`/`SpawnService`/`TickEvents` do not apply — this is an input/UI patch on `CardGraphics.OnPointerClick`, not a card-data or spawn mod. Correctly declares `CSFFModFramework` SoftDependency; no ModLoaderVerison/ModEditorVersion; no deprecated/dangerous patterns (no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes — the patched handler has a fast no-modifier early-return).

---

## Phase 0: Stabilize  *(skipped — audit score ≥ 8 and no open retrospectives)*

> Nothing blocks new work. Both carry-overs (W1 log gap, QT-1 runtime confirmation) are closed as of 2026-08-04.

---

## Phase 1: Foundation  *(complete — closed 2026-08-04)*

> Table-stakes for a healthy, maintainable input utility.

| Item | Type | Priority | Complexity | Status |
|------|------|----------|------------|--------|
| Add `else { Logger.LogError("CardGraphics.OnPointerClick not found — QuickTransfer inactive."); }` after the method-lookup `if` (`QuickTransferPatch.cs:40-48`) | Reliability / log-gap (W1) | P1 | Quick | ✅ Fixed 2026-08-04 |
| In-game confirm the QT-1 fix — Full Stack Mode, near-full destination, Ctrl+Shift+Right-Click a large stack; verify no spin/spam + `[QT] No progress` log | Runtime verification | P1 | Quick | ✅ Confirmed 2026-07-30 (playthrough T2.45) |
| Versions already synced at 1.7.5 (ModInfo / Plugin.cs / README) — keep synced via `Update-ModVersion.ps1` on any bump | Version hygiene | P1 | Quick | ✅ Synced |
| (Optional) Scope the `FindObjectsOfType` scan to the source slot's children if large individual-card batches ever feel sluggish (`QuickTransferPatch.cs:241`) | Performance (W2, deferred) | P2 | Medium | Open — deferred/optional |

**Localization note**: No `Localization/` folder — correct today (all player-facing text is 2 hardcoded overlay strings; preflight E16 is a documented false positive). A `SimpCn.csv` is *not* needed; the only path that would add localization is the "localize the overlay" idea below.

---

## Phase 2: Core Expansion

> The most impactful additions that extend the transfer UX without new game hooks. All are pure-C#, drawn from `Documentation/Ideas/QuickTransfer/IDEAS.md`.

### Configurable transfer mouse button
**What**: Replace the hardcoded right-click gate (`buttonInt != 1`) with a `ConfigEntry` for the trigger button.
**Why**: Supports left-handed / remapped mice and lets players move the trigger off right-click to dodge collisions with other input mods.
**Requires**: none
**Complexity**: Quick

### "Transfer half" relative preset
**What**: A config-selectable relative mode where one combo transfers `ceil(count/2)` of the clicked stack.
**Why**: Fills the gap between the "10" preset and the "all" sentinel for large stacks, without opening config.
**Requires**: none
**Complexity**: Quick

### Live-overlay clarity: combo label + reset-to-defaults
**What**: (a) Append the armed combo to the overlay (`Quick Transfer: 10 (Ctrl)`) so it's unambiguous when Shift/Ctrl presets share a value; (b) add a bindable "reset presets to defaults" action (Shift→5 / Ctrl→10 / Amount→5).
**Why**: The in-game Plus/Minus adjust makes it easy to lose track of preset state; both are one-file changes in `Plugin.Update` reusing `ShowNotification`.
**Requires**: none
**Complexity**: Quick

---

## Phase 3: Integration & Depth

> Cross-mod hooks and deeper transfer logic for experienced players.

### Drain matching stacks (Take-All by stack)
**What**: After exhausting the clicked slot, re-scan the same container for slots holding the same `UniqueID` and continue transferring; relax `ReferenceEquals(cardSlot, sourceSlot)` to "same container, same UniqueID."
**Why**: Turns bulk transfer into a true chest-empty / fire-load action instead of one-stack-at-a-time.
**Requires**: none (reuses existing candidate scan)
**Complexity**: Medium

### RepeatAction ↔ QuickTransfer shared input helper
**What**: Factor QT's modifier-state detection (`IsModifierKeyHeld` / Ctrl+Shift resolution) into a shared framework `Api.Input`-style helper both mods consume.
**Why**: Makes the documented QT↔RA modifier-collision impossible by construction; low-cost precursor to the eventual QoL_Utilities merge.
**Requires**: Coordination with CSFFModFramework (new surface) + RepeatAction
**Complexity**: Medium

### Smart_Inventory item-lock respect
**What**: Soft-detect a lock marker on `CardModel` (or an SI API) inside `IsValidCandidate` and skip locked cards from a batch — reflection-probe only, no hard `[BepInDependency]`.
**Why**: Smart_Inventory's backlog explicitly asks for it; keeps QT standalone while cooperating when SI is present.
**Requires**: Smart_Inventory present at runtime (soft)
**Complexity**: Medium

### WDI station requirement-slot guard
**What**: Verify QT's re-fired right-click behaves on WDI "Water-Driven Workshop" stations that route right-click through blueprint requirement slots (`ContainedBlueprintCardsWarpData`); add a graceful no-op if a requirement slot is the source rather than plain storage.
**Why**: Prevents a surprising "card consumed into recipe" edge case on modded stations.
**Requires**: WDI installed for verification
**Complexity**: Medium (verification-first)

---

## Phase 4: Polish

> Text and UX finish. No art applies (card-less mod).

| Item | What | Complexity |
|------|------|------------|
| Localize the overlay | Add a minimal language switch for the two `OnGUI` strings ("Quick Transfer: N" / "Quick Transfer: All") — advances the repo-wide CN localization push | Quick |
| Batch feedback parity | Play one summary chime per batch via the existing `ShowNotification` site so bulk transfers aren't silent past the first vanilla click | Quick |

---

## Long-term Vision

> Where QuickTransfer should be at v2.0.

By v2.0 QuickTransfer becomes the definitive inventory-movement QoL layer: presets plus a relative "half" mode, container-drain by UniqueID, and a graceful, documented cooperation story with the other input/inventory mods (RepeatAction modifier-collision solved by a shared framework helper; Smart_Inventory locks respected; modded stations verified). It stays a pure input utility — no card data, no game-state mutation — and its reliability story is airtight: every patch-resolution failure logs, and the batch loop is progress-aware (QT-1) and capacity-aware.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **Cap the batch at the destination's free capacity** — proactively stop at "as many as fit" instead of relying on vanilla to bounce the tail; natural once drain-matching lands. Needs a decision on whether the destination is knowable before the first vanilla move resolves.
- **Tag-filtered bulk transfer** (transfer all `tag_Fuel` / all food) — extend the candidate scan from "same UniqueID" to "shares a chosen CardTag"; big fire-loading / chest-sorting win, but needs an input-design decision for how the player picks the tag.

These live in `Documentation/Ideas/QuickTransfer/IDEAS.md` (already specced).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new feature phase | Run `/audit-mod QuickTransfer` and update this roadmap |
| Game version update | Run `/update-mod-version`, re-verify `CardGraphics.OnPointerClick` resolves (W1 log makes this diagnosable), re-run `/diagnose-log` |
| After fixing a warning/defect | Run `/critical-analysis QuickTransfer` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo QuickTransfer` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod QuickTransfer         — full health check, updates .audit/
/critical-analysis QuickTransfer — adversarial review
/build-mod QuickTransfer         — build Release DLL
/deploy-mods QuickTransfer       — build + deploy to game
/update-mod-version QuickTransfer <ver> — bump version in all 3 files
/export-to-repo QuickTransfer    — push to public repo
```
