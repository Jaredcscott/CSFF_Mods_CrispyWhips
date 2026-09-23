# Roadmap: QuickTransfer
Version at time of writing: 1.8.0
Date: 2026-09-15
Audit score: 10/10 (consolidated 2026-09-15)

## Current State

**Theme**: A pure input/QoL utility for Card Survival: Fantasy Forest - bulk-transfer card stacks into any container with modifier+right-click, with configurable presets, a live on-screen indicator, a half preset, drain-matching-stacks, a configurable trigger button, and a localized overlay. It never adds card data or mutates game state; it replays the game's own `CardGraphics.OnPointerClick`, which keeps it container-agnostic.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images. 4 C# source files (`Plugin.cs`, `Patcher/QuickTransferPatch.cs`, `OverlayText.cs`, `GlobalUsing.cs`) + a `Localization/` folder (7 keys, EN/CN).

**Stability**: 10/10 - 0 CRITICAL, 0 design gaps, 0 open warnings. The two warnings raised in the 2026-09-15 pass are resolved and committed (D17 silent-catch breadcrumb on `OverlayText.Format()`; G5 per-cause warn-once breadcrumbs on the four post-intent-gate reflection nulls in `OnPointerClick_Prefix`, both in commit 12174e9af). One optional, well-mitigated performance note (the candidate-scan scene walk) is carried in the backlog below, not a defect.

**Open work**: None on QuickTransfer's own code. `Documentation/Retrospectives/INDEX.md` carries one 🟡 Pending row (`wikimod-old-save-load-crash.md`) that names "Quick Transfer found nothing" only as a diagnostic symptom of a third-party mod (WikiMod) + framework compatibility bug; the root cause and fix live in `CSFFModFramework`/WikiMod, not in this mod. The QT-1 refused-destination batch-stop was runtime-confirmed in-game 2026-07-30 (playthrough T2.45).

**Framework compliance**: On the current framework API for what a utility needs. Uses the framework `Reflect` helper to locate `CardGraphics.OnPointerClick` at load (`SoftDependency` on `crispywhips.CSFFModFramework`); explicit `harmony.Patch` (not `PatchAll()`) wrapped per-class in try/catch; `OnDestroy` unpatches. The content-mod Tier 2 services (`ActionRouter` / `SpawnService` / `TickEvents` / `ContentModPlugin`) are correctly N/A - this mod has no cards, drops, spawns, or ticks to route. No deprecated/dangerous patterns: no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes (the `OnPointerClick` prefix early-returns on non-modifier/non-configured-button clicks), no `ModLoaderVerison`/`ModEditorVersion`, no manual injection.

---

## Phase 0: Stabilize  *(skipped - audit score >= 8 and no open QuickTransfer retrospectives)*

Nothing to stabilize. The mod is release-ready at 1.8.0.

---

## Phase 1: Foundation

> Table-stakes hygiene. Already satisfied - listed for the maintenance cadence.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Version sync (ModInfo / Plugin.cs `PluginVersion` / README all read 1.8.0) | Version hygiene | P1 | DONE |
| Chinese localization (`SimpEn.csv` + `SimpCn.csv`, 7/7 keys, parity CLEAN) | Localization | P1 | DONE (v1.8.0) |
| Warn-once breadcrumbs on non-throwing reflection nulls (G5) | Diagnosability | P1 | DONE (12174e9af) |
| D17 silent-catch breadcrumb on `OverlayText.Format()` | Diagnosability | P1 | DONE (12174e9af) |
| Deploy + publish the 2026-09-15 hardening | Release hygiene | P1 | Quick (diagnostics-only; last publish 2026-09-11, last deploy 2026-09-13, both predate 12174e9af) |

---

## Phase 2: Core Expansion

> QuickTransfer ships no content and, by design, never will (it is a pure input utility). "Expansion" here means new transfer behaviors. Every candidate below is a Medium-Term idea whose design decision must be made before it is built - none is dispatchable as-is.

### Reverse / pull-from-container modifier
**What**: A push-vs-pull direction for bulk transfer. The prefix already captures the clicked slot, so push-vs-pull is just reading which side the source slot is on.
**Why**: Completes the transfer loop symmetrically (bulk-empty a container, not only bulk-fill it).
**Requires**: An input-grammar decision first - a 4th combo (e.g. Alt+Right-Click = pull) vs context detection (source slot inside the container => pull) - chosen so it does not collide with the Ctrl/Shift/Ctrl+Shift triad.
**Complexity**: Medium

### Tag-filtered bulk transfer (all fuel / all food / a chosen CardTag)
**What**: Extend the candidate scan from "same UniqueID" to "shares a chosen CardTag" (e.g. every `tag_Fuel` into the fire in one action). Reads `CardModel` CardTags via `GetMemberValue`.
**Why**: The single biggest QoL step beyond same-stack transfer.
**Requires**: A UX decision - a held meta-modifier that switches from "this stack" to "this tag," and how the player picks the tag.
**Complexity**: Medium

### Cap the batch at the destination's free capacity
**What**: Read the destination's remaining capacity and stop cleanly at "as many as fit" instead of relying on vanilla to bounce the tail. Natural follow-up now that the reactive spin is fixed (v1.7.4).
**Why**: Removes the last case where the batch does visible no-op work.
**Requires**: A decision on how to observe capacity - QuickTransfer never chooses the destination (it re-fires the vanilla right-click), so capacity may only be observable after the first vanilla move resolves; derive the target from the first landing slot, or leave capacity to vanilla.
**Complexity**: Medium

*Also in the Medium-Term backlog (design decision needed, see `Documentation/Ideas/QuickTransfer/IDEAS.md`): per-container transfer-amount memory, quality/durability threshold filter, undo last batch transfer, container hotkeys, container blacklist/whitelist.*

---

## Phase 3: Integration & Depth

> Cross-mod hooks. All are soft-detect only - QuickTransfer must never take a hard `[BepInDependency]` on another content mod.

### Shared framework modifier-state helper (RepeatAction)
**What**: Factor QT's modifier detection (`IsModifierKeyHeld` / Ctrl+Shift resolution) into a framework `Api.Input`-style helper both QuickTransfer and RepeatAction consume.
**Why**: Makes the documented QT<->RA modifier-collision impossible by construction; low-cost precursor to an eventual QoL_Utilities merge without committing to it.
**Requires**: Coordination with CSFFModFramework (produces the helper) and RepeatAction.
**Complexity**: Medium

### Smart_Inventory item-lock respect
**What**: Reflection-probe a lock marker on `CardModel` (or an SI API) inside `IsValidCandidate` and skip locked cards from the batch. Soft-detect only.
**Why**: Prevents a bulk transfer from moving cards a player has deliberately locked.
**Requires**: Smart_Inventory to expose (or already carry) a detectable lock marker.
**Complexity**: Medium

### WDI station requirement-slot guard
**What**: Verify that re-firing right-click on a card sitting in a WDI Water-Driven Workshop requirement slot (`ContainedBlueprintCardsWarpData`) does not consume it into the recipe; add a graceful no-op if the source is a requirement slot rather than storage.
**Why**: Protects a real cross-mod interaction. Verification-first - never tested against a live station.
**Requires**: A live WDI station to test against.
**Complexity**: Medium

---

## Phase 4: Polish

> Art, animation, and text. Mostly N/A for a card-less utility.

| Item | What | Complexity |
|------|------|------------|
| Custom art | N/A - 0 CardData, 0 images. The mod's only UI is the `OnGUI` overlay text | - |
| GIF animation | N/A - no structures/cards to animate | - |
| Description accuracy | DONE - README/ModInfo spot-checked against `Config.Bind` defaults in the 2026-09-15 pass; every numeric claim matches | - |
| (Optional perf) Scope the candidate scan to the source slot's children | `FindFirstCandidate` (`QuickTransferPatch.cs:373`, call sites `:239`/`:249`) runs a full active-scene `FindObjectsOfType`; well-mitigated by candidate caching (fires once for the common stacked case) and a self-terminate. Only a large non-stacked "All"/Ctrl+Shift transfer produces N scans across N frames. Build only if that worst case is ever observed to feel sluggish | Medium (optional) |

---

## Long-term Vision

> Where this mod should be at v2.0.

QuickTransfer's natural endpoint is a complete, container-agnostic bulk-movement grammar: push and pull, by-stack and by-tag, with a clean stop at the destination's real capacity - all still riding the vanilla `OnPointerClick` path so it never has to know about any specific container or mod. The single largest justified addition is the reverse/pull modifier (it doubles the mod's utility for the cost of reading which side the source slot is on); tag-filtered transfer is the next after that. Beyond those, the mod's biggest strategic move is not a feature but a consolidation: a QoL_Utilities merge with RepeatAction behind a shared framework `Api.Input` helper, which removes the standing modifier-collision risk and gives both mods one config surface.

**Potential major additions** (not yet justified - revisit after Phase 3):
- Reverse / pull-from-container modifier - the highest-value single addition; fits the "container-agnostic movement" theme exactly.
- Tag-filtered bulk transfer - turns the mod from "move this stack" into "move this category," the natural depth step for an inventory-management utility.
- QoL_Utilities merge with RepeatAction - fits the theme (both are modifier+input QoL) and eliminates the documented cross-mod modifier collision by construction.

These live in `Documentation/Ideas/QuickTransfer/IDEAS.md` (Medium-Term / Long-Term tiers) with their design decisions noted.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new transfer-behavior phase | Run `/audit-mod QuickTransfer` and update this roadmap |
| Game version update | Run `/update-mod-version QuickTransfer`, refresh `lib/Assembly-CSharp.dll` (or regenerate the nstrip variant it binds), check CLAUDE.md EA version notes, re-run `/diagnose-log` |
| After fixing an issue | Run `/critical-analysis QuickTransfer` to verify the fix (also re-certifies the currently-stale 2026-09-05 critical-analysis against 1.8.0) |
| After a feature phase | Run `/export-to-repo QuickTransfer` and bump the version - the 2026-09-15 hardening is deployed-but-not-yet-published |

---

## Skill Cheatsheet for This Mod

```
/audit-mod QuickTransfer          - full health check, updates .audit/
/critical-analysis QuickTransfer  - adversarial review (re-certify against 1.8.0)
/code-quality QuickTransfer       - C# reliability/maintainability scan
/build-mod QuickTransfer          - build Release DLL
/deploy-mods QuickTransfer        - build + deploy to game
/update-mod-version QuickTransfer <ver> - bump version in all 3 files
/export-to-repo QuickTransfer     - push to public repo
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
