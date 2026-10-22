# Roadmap: QuickTransfer
Version at time of writing: 1.9.1
Date: 2026-10-20
Audit score: 10/10 (consolidated 2026-10-20)

## Current State

**Theme**: A pure input/QoL utility for Card Survival: Fantasy Forest - bulk-transfer card stacks into or out of any container with modifier+right-click, with configurable presets, Half / AllButOne / Full Stack modes, a live localized on-screen indicator, drain-matching-stacks and non-piling widening, a tag filter, a spoiled-only filter, a hover-and-press hotkey, one-level undo, and per-container exclude/remember settings. It never adds card data or mutates game state; it replays the game's own `CardGraphics.OnPointerClick`, which keeps it container-agnostic.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images. Pure C# (Plugin.cs, Patcher/QuickTransferPatch.cs, Patcher/ContainerSettings.cs, RepeatActionBridge.cs, UtilityBeltLockBridge, OverlayText.cs) + a `Localization/` folder (18 keys each, EN/CN).

**Stability**: 10/10 - 0 CRITICAL, 0 design gaps, 0 open code warnings. The prior pass's one mechanical contradiction (a Utility-Belt-locked *clicked* card could leave its container in a batch) was fixed in commit `643011de7` (2026-10-20 09:23), matching critical-analysis's recommended fix and the CHANGELOG `[Unreleased]` entry. Reflection-path discipline is exemplary (every non-throwing exit warns once per cause, latched per cause).

**Open work**: None on QuickTransfer's own code. Standing items are all operational/tracker, not defects: T2.668 (locked-clicked-card fix) pending human in-game confirmation; T2.333 (Workshop batch scan cost) and T2.1267 (Transfer Hotkey text-box guard, harness-unstable) carried open. `Documentation/Retrospectives/INDEX.md` carries no QuickTransfer defect (`wikimod-old-save-load-crash` names Quick Transfer only as a diagnostic symptom of a WikiMod/framework bug).

**Framework compliance**: On the current framework API for what a utility needs. `SoftDependency` on `crispywhips.CSFFModFramework`; explicit `harmony.Patch` (not `PatchAll()`) wrapped per-class in try/catch; `OnDestroy` unpatches. Content-mod Tier 2 services (`ActionRouter` / `SpawnService` / `TickEvents` / `ContentModPlugin`) are correctly N/A - this mod has no cards, drops, spawns, or ticks. No deprecated/dangerous patterns: no `DropCollectionGuardPatch`, no unfiltered hot-path mutation, no `ModLoaderVerison`/`ModEditorVersion`, no manual injection.

---

## Phase 0: Stabilize  *(skipped - audit score 10/10 and no open QuickTransfer retrospectives)*

Nothing to stabilize. The mod is release-ready at 1.9.1.

---

## Phase 1: Foundation

> Table-stakes hygiene. Already satisfied - listed for the maintenance cadence.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Version sync (ModInfo / Plugin.cs `PluginVersion` / README all read 1.9.1) | Version hygiene | P1 | DONE |
| Chinese localization (`SimpEn.csv` + `SimpCn.csv`, 18/18 keys, parity CLEAN) | Localization | P1 | DONE |
| Publish 1.9.x (Nexus holds 1.8.1; A-to-C ship repack pending, MUM embed behind) | Release hygiene | P1 | Blocked on peer work |

---

## Phase 2: Core Expansion

> QuickTransfer ships no content and, by design, never will (it is a pure input utility). "Expansion" means new transfer behaviors. **The transfer grammar is complete** - push/pull, by-stack/by-tag, half/all-but-one, spoiled-only, drain/widen, hover hotkey, undo, per-container settings, Full Stack, middle-click trigger, and the Repeat Action / Utility Belt bridges all shipped. No new transfer behavior is currently justified.

### Cap the batch at the destination's free capacity - CONSIDERED, NOT SELECTED
**Status**: Ruled out (MASTER_PLAN Considered table). QuickTransfer re-fires the vanilla click and never chooses the destination, so capacity is observable only after the first move; the 3-strike stop already ends a refused batch and the overlay reports how many moved. Revisit only if a player reports the refused-move feedback firing three times.

---

## Phase 3: Integration & Depth

> Cross-mod hooks. All soft-detect only - QuickTransfer never takes a hard `[BepInDependency]` on another content mod.

### Repeat Action key sharing - SHIPPED
QuickTransfer yields its count keys to Repeat Action and reads Repeat Action's count keys live (`RepeatActionBridge`, GUID `crispywhips.repeat_action`).

### Utility Belt lock skip - SHIPPED
A batch never moves a card locked in Utility Belt (`UtilityBeltLockBridge`, `Utility_Belt.LockApi.IsLocked`); the clicked-card gate was closed in `643011de7`. Utility Belt in turn consumes QuickTransfer's public `TryClickMoveOne`/`LastRefusal` API for Auto-Pull and Store Products.

---

## Phase 4: Polish

> No art, animation, or description surface - the mod ships zero cards. The only polish axis is diagnosability/text.

| Item | What | Complexity |
|------|------|------------|
| Carry T2.668 to human in-game confirmation | Confirm the locked-clicked-card fix behaves in a real game (code already shipped + diff-verified) | Quick (human step) |
| Revisit T2.333 / T2.1267 if a player surfaces them | Workshop batch scan cost; Transfer Hotkey text-box guard | Medium |

---

## Long-term Vision

> Where this mod should be at v2.0.

QuickTransfer is at its natural endpoint: a complete, container-agnostic bulk-transfer grammar with two optional-mod bridges, localized, 10/10. v2.0 is not a content milestone but a stability line - the point at which the open verification-debt rows (T2.668, T2.333, T2.1267) are all human-confirmed and 1.9.x is published on Nexus. No new transfer behavior is justified by current scope.

**Potential major additions** (not yet justified - revisit only on player demand):
- Capacity-aware batch cap - ruled out in the Considered table (architecture re-fires the vanilla click; capacity is only observable post-move).

Any of these worth building belongs in the mod's plan under `Documentation/Plans/QuickTransfer/`; never create a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any behavior change | Run `/audit-mod QuickTransfer` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After fixing an issue | Run `/critical-analysis QuickTransfer` to verify the fix |
| When peer A-to-C repack unblocks | `/export-to-repo QuickTransfer` + `/nexus-publish upload QuickTransfer`, bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod QuickTransfer         - full health check, updates .audit/
/critical-analysis QuickTransfer - adversarial review
/build-mod QuickTransfer         - build Release DLL
/deploy-mods QuickTransfer       - build + deploy to game
/update-mod-version QuickTransfer <ver> - bump version in all 3 files
/export-to-repo QuickTransfer    - push to public repo
/nexus-publish upload QuickTransfer - publish to Nexus
```

---

## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b. Append-only: never reword, reorder or delete an entry.
> `/roadmap` Step 7 preserves this section verbatim when it overwrites the rest of this file.

### 2026-09-08 - Audit_Remediation_Plan (closeout)
- **Verdict:** 7 of 7 feature rows built, all in v1.8.0: N1/N3/N4/N7 on 2026-09-07 (commit 73ed3b6a8) and N2/N5/N6 on 2026-09-08 (commit cae637da4). The Fixes section was empty by design (0 CRITICAL / 0 Design Gap at the 2026-09-05 promotion; W1 and W2 stay in the audit trail as non-sequenceable warnings). The pack header still read `4 open` from its 2026-09-07 refresh while all four prompts had shipped, so the cheap signal lagged the tree by one commit.
- **Disposition:** ARCHIVE to `Documentation/Design/QuickTransfer_Audit_Remediation_As_Built.md` (the plan doc with an archive banner, its three remaining rows marked SHIPPED, and the drained pack appended as an appendix). The plan carries five load-bearing premise corrections that exist nowhere else in discoverable form: N1 owes the full count on a non-right trigger and Left is excluded; N7 fired one sound per card and shipped inverted; N6 must measure progress on the sibling slot; N2 resolves its sentinel in the click prefix; N5's CSV path is viable for a content-less mod. Pack `Audit_Remediation_Plan_Implementation_Prompts.md` deleted (drained, all 4 prompts built).
- **Pruned:** nothing from the live plan before archiving; the plan left the tree whole. `Documentation/Ideas/QuickTransfer/IDEAS.md`: the seven Near-Term rows (all shipped) moved to its Shipped section carrying the corrected premises, so `/audit-to-plan` cannot re-promote them; `.audit/ideas.md` (gitignored, local) rows 2/5/6 struck the same way, and its Pattern Gaps rows marked closed.
- **Evidence:** `grep -nE 'HalfSentinel|ParentLayoutGroup|DrainMatchingStacks|OverlayText.Get' QuickTransfer/Plugin.cs QuickTransfer/Patcher/*.cs` hits: N2 `HalfSentinel` at Plugin.cs:76 with the prefix resolution at QuickTransferPatch.cs:331; N6 `DrainMatchingStacks` at Plugin.cs:65/171 with the `ParentLayoutGroup` reads at QuickTransferPatch.cs:419-420; N5 `QuickTransfer/OverlayText.cs` plus `Localization/SimpEn.csv` and `SimpCn.csv` at 7 keys each. `git status --short -- QuickTransfer/` clean. Built `bin/Release/Quick_Transfer.dll` MD5 A0B0E0CC41E7B04B811D60699B4AB470 equals the deployed `BepInEx/plugins/Quick_Transfer/Quick_Transfer.dll`, and the deployed folder holds both CSVs. `Development_Tools/Check-LocalizationParity.ps1`: QuickTransfer 7/7 CLEAN. `ModInfo.json`, `Plugin.cs` `PluginVersion` and `README.md` all read 1.8.0. Every awaiting-verification item's tracker row is in HEAD (`git show HEAD:.claude/playthrough-test-status.json` contains T2.179-T2.182 and T2.213-T2.215), so lifecycle gate 2 holds.
- **Verification debt:** T2.179, T2.180, T2.181, T2.182 (N1/N3/N4/N7), T2.213 (N2 Half preset), T2.214 (N6 Drain Matching Stacks), T2.215 (N5 localized overlay), all `pending` in `.claude/playthrough-test-status.json`; the seven rows' `source` citations were repointed at the archive in the same commit. v1.8.0 is built and deployed but NOT published: the last `/export-to-repo` was 2026-08-24, so the Maintenance Calendar's "After Phase 2 complete" publish trigger is now live.

### 2026-09-26 - Content_Update_Plan (QuickTransfer 1.9.0 closeout)
- **Verdict:** 5 of 5 rows built: enabler 0.1 as TestHarness 1.3.0 (`ui/click`/`ui/drag` take `button`, `hold`, and `card` on click); feature 1 (pull) needed no code; features 2 (Tag Filter) and 3 (Prioritize Most Spoiled) are new code in QuickTransfer 1.9.0; feature 4 (WDI guard) needed no guard because the Workshop has no requirement slot. The build corrected four plan premises (the Item/Inventory mapping in `SwapCard` was backwards; `tag_Fuel` is not a fuel filter at runtime, so the default became `tag_HumanFood,tag_FoodRaw`; the spoilage direction must come from the authored rate, not the live one; the Workshop keeps one card per slot, so pulling from it needs Drain Matching Stacks) and fixed one 1.8.0 defect the scenarios exposed (the overlay hint clobbered every notice while the modifier was held).
- **Disposition:** ARCHIVE to `Documentation/Design/QuickTransfer_Content_Update_As_Built.md`: the plan kept whole under an "As built: what changed from the plan" section that wins where they disagree. No `_Implementation_Prompts.md` pack was ever written, so there was nothing to drain.
- **Pruned:** `Documentation/Ideas/QuickTransfer/IDEAS.md`: the pull, tag-filter and WDI rows moved to Shipped with their corrected premises; the threshold-filter row keeps only its unbuilt pass/fail half. This file's Phase 2 pull and tag rows are marked SHIPPED and the Phase 3 WDI row CLOSED.
- **Evidence:** Harness runs against the live game (EA 0.68b), fixed build DLL MD5 98a7c2e384d541bafd5874eb287a2200 in both QuickTransfer/bin/Release and the deployed plugin folder: T2.330 PASS 190005, T2.331 PASS 192711 (41/41), T2.332 PASS 194716, T2.213 PASS 195049, T2.333 PASS 195620, T2.215 PASS 195904 (regression on the overlay change); red demonstration RED-T2.330 (pull click forced to Left) FAILED as required at 194240. The first runs of T2.332 (193349), T2.213 (193712) and T2.333 (194034) failed and each failure is a correction above. Runtime tag membership from a harness sweep of 4,328 loaded CardData. Pester: Mod-Regression + Csproj-ContentIncludes 91/91, Deploy-Mods + Stable-Layout 62/62, PlaythroughRow-Citations 13/13 (re-run in the release gate below). QuickTransfer builds with 0 warnings, 0 errors.
- **Verification debt:** T2.331 (human half: a real keyboard's Caps Lock must count while HELD, not while toggled on), T2.334 (human half: the reset confirmation and post-click count stay up while the combo is held), T2.214 (split piles need a hand-made split; new clause (f) for Prioritize Most Spoiled), T2.179 (middle-click, automatable now, no scenario yet). All `pending` in `.claude/playthrough-test-status.json`. QuickTransfer 1.9.0 is deployed but the MUM suite embed still holds 1.8.1 until the next suite repack.

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 19 features and 8 Wave 0 items checked; 2 verified done (Features 6 and 7), 7 built and awaiting verification (Features 1 to 5, 8, 9), 10 open (Features 10 to 19), Wave 0 open W0.4 and W0.8 (W0.1 to W0.3, W0.5 to W0.7 answered); pack 15 prompts, 14 open (Prompts 2 to 15), Prompt 1 drained.
- **Disposition:** KEEP IN PLACE (open work remains: the pack's next build is Prompt 2, Feature 10).
- **Pruned:** plan sections 6 and 7 and their Selected rows 6 and 7, the Risk register row for default-on widening and the Feature 6 reference in the prefix-risk row; pack Awaiting verification bullets for Features 6 and 7 (the Feature 8 and 9 bullets now say their scenario rows are confirmed). Numbering kept.
- **Evidence:** tracker read by script: T2.656 (`confirmedItems/qt_all_but_one_preset`), T2.333 (`confirmedItems/qt_wdi_workshop_storage`), T2.657 (`confirmedItems/qt_spoiled_only`) and T2.1266 (`confirmedItems/qt_hover_transfer_hotkey`) all carry `confirmedOn` 2026-10-04T22:57:38.000Z; T2.331, T2.650 to T2.655, T2.658 and T2.1267 are `pending`. Code probes (grep over `QuickTransfer/**/*.cs`, control: each symbol hit its own file): `AllButOne` in 3 files, `AllButOneSentinel` 2, `WidenForNonPilingCards` 2, `CannotPile` 1, `SkipClick` 1, `SpoiledOnlyKey` 1, `IsSpoiledEnough` 1, `TransferHotkey` 1, `TextInputFocused` 1, `RequestHoverTransfer` 2, `RepeatActionBridge` 2. Version 1.9.0 in `ModInfo.json`, `Plugin.cs` `PluginVersion` and `README.md`; `CHANGELOG.md` top heading `## [1.9.0]`; `.claude/nexus-publish-status.json` QuickTransfer `version` 1.8.1 (unpublished). `git log -- QuickTransfer/` shows the feature commits `b604ab8d4` (4), `61dee668c` (5), `470d10e03` (6), `903fe7b46` (7), `a0d95b6f5` (8), `f8bfafe2c` (9). Every `T<n>.<n>` id left in the plan and pack exists in the tracker (script, 0 missing). Not re-run: the scenarios themselves (no game in this pass), Pester gates.
- **Verification debt:** T2.650, T2.651, T2.652, T2.653, T2.654, T2.655, T2.331, T2.658, T2.1267, all `pending`.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** counts: 13 feature sections (1, 2, 8, 9, 11 to 19) and 8 Wave 0 items checked against `a11a1fd6f`. Verified done: Feature 2 (T2.653 confirmed; its section stays as a pointer because the Utility Belt pack's Prompt 14 reads it) plus the W0.1 and W0.6 findings of done Features 3 and 2. Built and awaiting verification: 6 (Features 1, 8, 9 and 19 with rows; Features 11 and 12 with no tracker row or scenario yet). Open: 6 features (13 to 18) and W0.4, W0.8. Failed 0, obsolete 0. Stale claims corrected 4: the Release tail's Chinese line listed the shipped Feature 11 and 12 overlay rows as still expected; sections 13, 16 and 18 cited Feature 3's deleted grounding; section 19 credited its code to `976e8321c`, whose QuickTransfer diff is README and CHANGELOG only (the `RepeatActionBridge.cs` change landed in `ac0e07fff`); pack Prompts 7 and 10 cited a section 11 Grounding that no longer exists. Pack: 11 open before and after (Prompts 3, 4, 6 to 13, 15; first entry Prompt 3, deps none, confirmed next), awaiting-verification bullets 6 to 4.
- **Disposition:** KEEP IN PLACE - 6 open feature rows of code and scenario work remain (Features 13 to 18), plus Prompt 4's scenarios for Features 11 and 12 and three ship prompts.
- **Pruned:** plan: Selected row 2, section 19's design prose (collapsed to its BUILT pointer), the Wave 0 findings subsections W0.1 and W0.6 (each bullet keeps its decision; full text at `189f8b99e`), the resolved Open question on the Exclude and Remember default keys, the Risk register rows for Feature 3 and for Chinese rows, and the sequencing paragraph's list of built files. Pack: the four DRAINED stubs (Prompts 1, 2, 5 and 14; numbering kept, Prompt 15 now depends on 13), and the Awaiting verification paragraph rewritten as four bulleted items each naming its row. Prompts 4, 7 to 10 and 13 now take a new row id from the QuickTransfer block of `.claude/tracker-id-blocks.txt` instead of the live maximum.
- **Evidence:** `git log --oneline 976e8321c..HEAD -- QuickTransfer/ TestHarness/client/scenarios/QT/` returned 0 commits (control: the same range over `Documentation/Plans/QuickTransfer/` returned 4, `a4f572ce5`, `b1c4eb423`, `df3cb326a`, `189f8b99e`). Symbol probe over `QuickTransfer/**/*.cs` excluding `obj/` (control: every symbol hit its own file): `IsExcluded`, `ToggleExcluded`, `ComboNow`, `RememberedAmount` in `Patcher/ContainerSettings.cs`; `ExcludeContainerKey`, `RememberAmountKey` in `Plugin.cs`; `RequestToggleExcluded`, `RequestToggleRemembered` in `Plugin.cs` and `Patcher/QuickTransferPatch.cs`; `Count Up Key` and `Count Down Key (Alt)` in `RepeatActionBridge.cs`; `IsPinnedCard`, `IsSpoiledEnough`, `RequestHoverTransfer` present. `QuickTransfer_Overlay_*` rows: 18 in `SimpEn.csv` and 18 in `SimpCn.csv`. Version 1.9.0 in `ModInfo.json`, `Plugin.cs` and `README.md`. Deployed `Quick_Transfer.dll` md5 `1e0c7574fe79f7d13574278ab51ef777`, equal to `git show ac0e07fff:QuickTransfer/bin/Release/Quick_Transfer.dll` and to `976e8321c`'s copy; its UTF-16 strings include `Count Up Key`, `Excluded Containers` and `Remember Amount Key`. Tracker read by script: T2.653, T2.179 and T2.180 in `confirmedItems`; T2.652, T2.658, T2.1267, T2.214, T2.334, T2.660 and T2.721 `pending`; T2.661 and T2.662 absent (control: T2.660 present); a grep of the tracker for `QuickTransfer/MASTER_PLAN.md Feature 11`/`12`, `ContainerSettings`, `Exclude Container` and `Remember Amount` returned 0 (control: `... Feature 10` returned 1). Unrecorded harness PASSes on disk: `t2-1267-...-20261007-055111` and `t2-214-...-20261007-031921`. Not run: scenarios, builds and gates beyond the four plan-hygiene Pester files.
- **Verification debt:** T2.652, T2.658, T2.1267, T2.721 (plan items), plus T2.214, T2.334 and T2.660 (release readiness), all `pending`. Features 11 and 12 have no row yet: the sweep asked the dispatcher to file one each from the QuickTransfer block.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** counts: 13 feature sections (1, 2, 8, 9, 11 to 19) and 8 Wave 0 items checked against `1405a21d4`. Verified done 1 (Feature 19: T2.721 confirmed 2026-10-09). Built and awaiting verification 3 (Features 1, 8, 9). Failed 2 (Features 11 and 12: their scenarios ran red, a scenario cfg defect, not a code one). Open 6 (Features 13 to 18) plus W0.4 and W0.8 (W0.1 to W0.3 and W0.5 to W0.7 answered). Obsolete 0. Stale claims corrected 2 (sections 11 and 12 said no scenario or row existed). Pack: 11 open before, 10 after (Prompt 6 folded into Prompt 3; Prompt 4 re-opened as a scenario fix and moved first); awaiting-verification bullets 4 to 3; a new Failed list of 1 bullet (Features 11 and 12).
- **Disposition:** KEEP IN PLACE - Features 13 to 18 remain to build and verify, Prompt 4 owes the two scenario fixes and re-runs, and three ship prompts remain.
- **Pruned:** plan: section 19 (Feature 19, VERIFIED-DONE, code `ac0e07fff`, gate and docs `976e8321c`, confirmed by tracker row T2.721 on 2026-10-09 from the six-rig run recorded in `51641ef64`), its Selected row and its Risk register row; three prose references to Feature 19 reworded. Pack: Feature 19's Awaiting verification bullet; Prompt 6 (Ship Phase C), folded into Prompt 3 because any repack takes HEAD's DLL, which already carries Features 11, 12 and 19. Prompt numbers kept (tracker rows T2.661 and T2.662 cite "plan Prompt 4"); dispatch order is now Prompt 4, Prompt 3, then 7 onward.
- **Evidence:** `git log --oneline a11a1fd6f..HEAD -- QuickTransfer/ TestHarness/client/scenarios/QT/ Documentation/Plans/QuickTransfer/` returned 4 commits, none touching QuickTransfer source (control: the same range over the plan folder returned `850b3dc9d`): `850b3dc9d` (plan), `48b0a0c1c` (T2.1267 scenario), `e57d44cfc` (T2.661 and T2.662 scenarios), `a47b5b8e3` (T2.214 scenario). Tracker read by script over `items` and `confirmedItems` by `id`: T2.721, T2.179, T2.180, T2.182, T2.213, T2.215, T2.330 to T2.333, T2.650, T2.651, T2.653 to T2.657, T2.659 and T2.1266 confirmed; T2.652, T2.658, T2.1267, T2.334, T2.660, T2.214, T2.661 and T2.662 pending; none of the eight is on the 2026-10-09 skeptic-rejected list. Harness reports under `TestHarness/client/reports/`: T2.661 FAIL 8 steps at `20261009-012638` and `-062432`, T2.662 FAIL 10 steps at `20261009-012821` and `-065453`, each failing first at the cfg baseline step (`ExcludeContainerKey.Value` / `RememberAmountKey.Value` read `None`); both scenarios' relaunch cfg put the key under `Keybindings` while `QuickTransfer/Plugin.cs` binds both under `Containers` (grep: 4 `Containers` binds). T2.1267 FAIL 4 steps at `20261009-075939` (rig 4, case (c), warning `could not read InGameCardBase.CardVisuals`); T2.214 FAIL 7 steps at `20261009-070901` (rig 5). Quick_Transfer.dll md5 by rig: main install and rigs 2, 3, 6, 7 `1e0c7574fe79f7d13574278ab51ef777` (equal to `ac0e07fff`'s committed build); rigs 4 and 5 a 61,440-byte `37b78245...` matching none of the last 12 committed builds and carrying no `ExcludeContainerKey` string (control: HEAD's build carries it 3 times), mtimes 2026-10-09 07:51 and 07:34. `.claude/nexus-publish-status.json`: QuickTransfer 1.8.1, UtilityBelt 0.1.0 with `UtilityBelt/bin/Release/` holding only `Repeat_Action.dll` and `Utility_Belt.dll`, so 1.9.0 is still unpublished. `git worktree list`: the main checkout only. Not run: scenarios, builds, deploys.
- **Verification debt:** T2.652, T2.658, T2.1267 (plan Features 1, 8, 9), T2.661, T2.662 (Features 11 and 12, scenario fix owed), and T2.214, T2.334, T2.660 (release readiness), all `pending`.
