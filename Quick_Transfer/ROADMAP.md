# Roadmap: QuickTransfer
Version at time of writing: 1.7.7
Date: 2026-09-05
Audit score: 10/10 (consolidated 2026-09-05)

## Current State

**Theme**: A pure input/QoL utility for Card Survival: Fantasy Forest - bulk-transfer card stacks into any container with modifier+right-click, with configurable presets and a live on-screen indicator. It never adds card data or mutates game state; it replays the game's own `CardGraphics.OnPointerClick`, which keeps it container-agnostic.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images. 3 C# source files (`Plugin.cs`, `Patcher/QuickTransferPatch.cs`, `GlobalUsing.cs`).

**Stability**: 10/10 - 0 CRITICAL, 0 design gaps, 2 warnings (one a documented preflight false positive, one a well-mitigated optional performance note). critical-analysis.md 2026-09-05: SOLID. code-quality.md 2026-09-05: 10/10.

**Open work**: None. Zero open/pending retrospectives (INDEX.md has no QuickTransfer/QT/plugin-GUID mentions). The QT-1 refused-destination batch-stop was runtime-confirmed in-game 2026-07-30 (playthrough T2.45).

**Framework compliance**: On the current framework API for what a utility needs. Uses the framework `Reflect` helper to locate `CardGraphics.OnPointerClick` at load (`SoftDependency` on `crispywhips.CSFFModFramework`, declared `Plugin.cs:7`); explicit `ApplyPatch` + `harmony.Patch` (not `PatchAll()`); `OnDestroy` unpatches. No deprecated/dangerous patterns: no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes (the `OnPointerClick` prefix early-returns on non-modifier/non-right-click clicks), no `ModLoaderVerison`/`ModEditorVersion`, no manual injection. The content-mod Tier 2 services (`ActionRouter`/`SpawnService`/`TickEvents`/`ContentModPlugin`) are correctly N/A - this mod has no cards, drops, spawns, or ticks to route.

---

## Phase 0: Stabilize  *(skipped - audit score >= 8 and no open retrospectives)*

Nothing to stabilize. The mod is release-ready at 1.7.7.

---

## Phase 1: Foundation

> Table-stakes hygiene. Already largely satisfied - listed for the maintenance cadence.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Keep ModInfo.json / Plugin.cs / README.md versions synced (all 1.7.7 now) | Version hygiene | P1 | Quick |
| Refresh `lib/Assembly-CSharp.dll` + rebuild on every game update (surfaces signature drift at compile time) | Game-update hygiene | P1 | Quick |
| Re-verify E16 stays a documented false positive after any preflight-heuristic change | Audit hygiene | P1 | Quick |

*No localization baseline is required today: the mod ships 0 localized strings. The only two player-facing strings are the `OnGUI` overlay labels, addressed in Phase 4.*

*Superseded 2026-09-08: v1.8.0 added the mod's first `Localization/` folder (7 keys, EN/CN parity CLEAN), so the localization parity gate now applies to this mod and preflight E16 is no longer a false positive here.*

---

## Phase 2: Core Expansion

> The most impactful QoL additions that extend the transfer loop. All are "ready to build, no design decision blocking" (from Documentation/Ideas/QuickTransfer/IDEAS.md, reconciled through v1.7.7).

### Configurable transfer mouse button
*SHIPPED v1.8.0 (2026-09-07, commit 73ed3b6a8) as `Transfer Mouse Button` (Right/Middle). Premise correction: only a right-click reaches `SwapCard`, so a non-right trigger owes the full count, and Left is excluded because vanilla routes it to `InspectCard`. Tracker T2.179-T2.182.*
**What**: Add a `ConfigEntry` for the trigger button and swap the one hardcoded comparison (`QuickTransferPatch.cs:77`, `buttonInt != 1`).
**Why**: Unblocks left-handed / remapped-mouse players and lets users dodge collisions with other input mods (the documented QT<->RepeatAction modifier concern). No new patch target.
**Requires**: none.
**Complexity**: Quick

### "Transfer half" relative preset
*SHIPPED v1.8.0 (2026-09-08, commit cae637da4) as `Ctrl+Shift Preset Mode` (All default / Half); the half count is resolved in the click prefix off the clicked slot, since the overlay has no slot in hand. In-game check T2.213 pending.*
**What**: A config-selectable relative mode where one combo returns `ceil(count/2)` of the clicked stack from `GetEffectiveTransferAmount` (`Plugin.cs:240`).
**Why**: Fills the gap between the "10" preset and the "all" sentinel for large stacks - the most-requested missing granularity.
**Requires**: none.
**Complexity**: Quick

### Drain matching stacks (Take-All by stack)
*SHIPPED v1.8.0 (2026-09-08, commit cae637da4) as `Drain Matching Stacks` (off by default). Relaxing the predicate alone would have stalled the batch after three moves: progress is now measured on the slot the moved card sits in. In-game check T2.214 pending.*
**What**: After exhausting the clicked slot, re-scan the same container for other slots holding the same `UniqueID` and continue; relax `ReferenceEquals(cardSlot, sourceSlot)` (`:261`) to "same container, same UniqueID." Config-gated.
**Why**: Turns "move this stack" into "move all of this item from this container" without leaving the pure-input model; reuses the existing candidate scan.
**Requires**: none (but pairs naturally with the Phase 4 scan-scoping perf fix).
**Complexity**: Medium

---

## Phase 3: Integration & Depth

> Cross-mod hooks that keep QuickTransfer a good citizen in the suite.

### Shared framework modifier-state helper (QT <-> RepeatAction)
**What**: Factor QT's modifier detection (`IsModifierKeyHeld` / Ctrl+Shift resolution, `Plugin.cs:228,240`) into a framework `Api.Input`-style helper both mods consume.
**Why**: Makes the documented QuickTransfer<->RepeatAction modifier collision impossible by construction, and is the low-cost precursor to an eventual QoL_Utilities merge without committing to it.
**Requires**: Coordination with CSFFModFramework (produces the helper) and RepeatAction (co-consumer).
**Complexity**: Medium

### Smart_Inventory item-lock respect
**What**: Reflection-probe a lock marker on `CardModel` inside `IsValidCandidate` and skip locked cards from the batch. Soft-detect only, no `[BepInDependency]` hard link.
**Why**: Prevents bulk-transfer from yanking cards a player deliberately locked. Keeps the SoftDependency-only posture.
**Requires**: A Smart_Inventory lock marker/API to probe.
**Complexity**: Medium

### WDI station requirement-slot guard
**What**: Verify re-firing right-click on a card in a WDI Water-Driven Workshop `ContainedBlueprintCardsWarpData` requirement slot does not consume it into the recipe; add a graceful no-op if the source is a requirement slot.
**Why**: Avoids a subtle destructive interaction with WDI stations. Verification-first - never tested against a live station.
**Requires**: A live WDI station to test against.
**Complexity**: Medium

---

## Phase 4: Polish

> The finishing touches that make the utility feel complete.

| Item | What | Complexity |
|------|------|------------|
| ~~Combo label in the live overlay~~ SHIPPED v1.8.0 (T2.179-T2.182) | Append `(Ctrl)`/`(Shift)`/`(Ctrl+Shift)` to the persistent hint (`Plugin.cs:201`) so `Quick Transfer: 10 (Ctrl)` disambiguates when Shift/Ctrl presets share a value - mirror the adjust path's existing suffixes. One-line change. | Quick |
| ~~"Reset presets to defaults" hotkey~~ SHIPPED v1.8.0 (T2.179-T2.182) | A bindable action that snaps Shift->5 / Ctrl->10 / Amount->5 and confirms via `ShowNotification`; touches only `Plugin.Update`. | Quick |
| ~~Localize the two overlay strings~~ SHIPPED v1.8.0 (T2.215; seven strings, not two) | Add a minimal language switch for the two `OnGUI` strings ("Quick Transfer: N" / "...: All"); this is the only path that would ever add a `Localization/` folder, and advances the repo-wide CN push. Config descriptions are BepInEx-owned, out of scope. | Quick |
| ~~Sound/feedback parity~~ SHIPPED v1.8.0 inverted (T2.179-T2.182): the batch fired one sound per card, so those are now muted and one replays at batch end | Play one summary chime per batch at the `ShowNotification` site in `OnPointerClick_Postfix` (the batch is silent past the first vanilla click sound). | Quick |
| (Optional perf) Scope the candidate scan | Scope `FindFirstCandidate`'s `FindObjectsOfType` (`QuickTransferPatch.cs:245`, call sites `:144`/`:153`) to the source slot's own children instead of the whole active scene. Bounded/self-terminating today; do only if a large non-stacked "All" transfer is ever observed to feel sluggish. summary.md W2. | Medium |

---

## Long-term Vision

> Where this mod should be at v2.0.

QuickTransfer's natural endpoint is a complete, configurable bulk-movement layer that stays a pure input utility: any trigger button, absolute or relative amounts, and "drain all matching" within a container - all while never choosing the destination or reimplementing the transfer path. The biggest addition that only becomes justified at that scale is a merge with RepeatAction into a single QoL_Utilities mod sharing one framework input helper; that is deliberately deferred until either mod needs a feature change anyway. Anything touching destination selection (container hotkeys, capacity-cap, blacklist/whitelist) requires a container-identity design the mod intentionally lacks today and should only be taken on if that model is added.

**Potential major additions** (not yet justified - revisit after Phase 3):
- Reverse / pull-from-container modifier - a 4th combo or context detection (source slot side) to pull as well as push; needs an input-grammar decision so it does not collide with the Ctrl/Shift/Ctrl+Shift triad.
- Tag-filtered bulk transfer (all fuel / all food by CardTag) - extends the candidate scan from same-UniqueID to a chosen CardTag; needs a UX decision for how the player picks the tag.

These live with full specs in `Documentation/Ideas/QuickTransfer/IDEAS.md` (Medium-Term / Long-Term tiers).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content/feature phase | Run `/audit-mod QuickTransfer` and update this roadmap |
| Game version update | Refresh `lib/Assembly-CSharp.dll`, rebuild, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After any transfer-path change | Re-confirm the `CannotBeTransferred` + CardType-8 guards in `IsValidCandidate` survive, and run `/critical-analysis QuickTransfer` |
| After Phase 2 complete | Run `/export-to-repo QuickTransfer` and bump the minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod QuickTransfer         - full health check, updates .audit/
/critical-analysis QuickTransfer - adversarial review
/code-quality QuickTransfer      - C# reliability/maintainability scan
/build-mod QuickTransfer         - build Release DLL
/deploy-mods QuickTransfer       - build + deploy to game
/update-mod-version QuickTransfer <ver> - bump version in all 3 files
/export-to-repo QuickTransfer    - push to public repo
```


## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b. Append-only: never reword, reorder or delete an entry.
> `/roadmap` Step 7 preserves this section verbatim when it overwrites the rest of this file.

### 2026-09-08 - Audit_Remediation_Plan (closeout)
- **Verdict:** 7 of 7 feature rows built, all in v1.8.0: N1/N3/N4/N7 on 2026-09-07 (commit 73ed3b6a8) and N2/N5/N6 on 2026-09-08 (commit cae637da4). The Fixes section was empty by design (0 CRITICAL / 0 Design Gap at the 2026-09-05 promotion; W1 and W2 stay in the audit trail as non-sequenceable warnings). The pack header still read `4 open` from its 2026-09-07 refresh while all four prompts had shipped, so the cheap signal lagged the tree by one commit.
- **Disposition:** ARCHIVE to `Documentation/Design/QuickTransfer_Audit_Remediation_As_Built.md` (the plan doc with an archive banner, its three remaining rows marked SHIPPED, and the drained pack appended as an appendix). The plan carries five load-bearing premise corrections that exist nowhere else in discoverable form: N1 owes the full count on a non-right trigger and Left is excluded; N7 fired one sound per card and shipped inverted; N6 must measure progress on the sibling slot; N2 resolves its sentinel in the click prefix; N5's CSV path is viable for a content-less mod. Pack `Audit_Remediation_Plan_Implementation_Prompts.md` deleted (drained, all 4 prompts built).
- **Pruned:** nothing from the live plan before archiving; the plan left the tree whole. `Documentation/Ideas/QuickTransfer/IDEAS.md`: the seven Near-Term rows (all shipped) moved to its Shipped section carrying the corrected premises, so `/audit-to-plan` cannot re-promote them; `.audit/ideas.md` (gitignored, local) rows 2/5/6 struck the same way, and its Pattern Gaps rows marked closed.
- **Evidence:** `grep -nE 'HalfSentinel|ParentLayoutGroup|DrainMatchingStacks|OverlayText.Get' QuickTransfer/Plugin.cs QuickTransfer/Patcher/*.cs` hits: N2 `HalfSentinel` at Plugin.cs:76 with the prefix resolution at QuickTransferPatch.cs:331; N6 `DrainMatchingStacks` at Plugin.cs:65/171 with the `ParentLayoutGroup` reads at QuickTransferPatch.cs:419-420; N5 `QuickTransfer/OverlayText.cs` plus `Localization/SimpEn.csv` and `SimpCn.csv` at 7 keys each. `git status --short -- QuickTransfer/` clean. Built `bin/Release/Quick_Transfer.dll` MD5 A0B0E0CC41E7B04B811D60699B4AB470 equals the deployed `BepInEx/plugins/Quick_Transfer/Quick_Transfer.dll`, and the deployed folder holds both CSVs. `Development_Tools/Check-LocalizationParity.ps1`: QuickTransfer 7/7 CLEAN. `ModInfo.json`, `Plugin.cs` `PluginVersion` and `README.md` all read 1.8.0. Every awaiting-verification item's tracker row is in HEAD (`git show HEAD:.claude/playthrough-test-status.json` contains T2.179-T2.182 and T2.213-T2.215), so lifecycle gate 2 holds.
- **Verification debt:** T2.179, T2.180, T2.181, T2.182 (N1/N3/N4/N7), T2.213 (N2 Half preset), T2.214 (N6 Drain Matching Stacks), T2.215 (N5 localized overlay), all `pending` in `.claude/playthrough-test-status.json`; the seven rows' `source` citations were repointed at the archive in the same commit. v1.8.0 is built and deployed but NOT published: the last `/export-to-repo` was 2026-08-24, so the Maintenance Calendar's "After Phase 2 complete" publish trigger is now live.
