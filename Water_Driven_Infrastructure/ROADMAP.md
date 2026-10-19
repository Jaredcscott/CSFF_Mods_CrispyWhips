# Roadmap: WaterDrivenInfrastructure
Version at time of writing: 1.12.12
Date: 2026-10-19
Audit score: 10/10 (PASS, release-ready) - see `.audit/summary.md`

## Current State

**Theme**: Late-game water-powered industry - sawmills, forges, grinding/stamp mills, ore sluices, fishponds, fish funnels, mill race outlets, plus retting pits and irrigation channels - built near rivers using water wheels and mill races. For a player past the early survival tier who wants large-scale manufacturing and a steady water supply at any outdoor location.

**Content**: 31 items / 35 blueprints / 21 structures (CT2) / 7 perks / 39 custom images

**Stability**: 10/10 (release-ready) - 0 CRITICAL, 0 confirmed design gaps, 0 broken promises, 0 open retrospectives. Build clean (0 errors / 0 warnings), bin/Release in sync, versions agree at 1.12.12, Chinese parity 673/673, cross-mod collisions 0 (17 mods / 2623 UIDs). `critical-analysis.md` is current with the latest source commit (2026-10-19) and returns SOLID. The two newest structures (Retting Pit, Irrigation Channel) are wired and mechanically verified against JSON + the `MillRaceNetwork`/`ActionInterceptPatch` C# gate, pending only human in-game confirmation (T2.1798, T2.1799). Perk tracking is complete: all 7 perk UIDs are present in `Features.json` (the prior "3 of 7" perks-report gap is stale/resolved).

**Open work**: none (all 4 WDI-tagged retrospectives are graduated/archived). One open tracker warning: **T2.601** reads `fail` on a stale premise (its run PASSED but was not recorded because the row still describes vanilla's `PartnerDuty_HarvestReeds` instead of WDI's own `wdiHarvestReedBed_Duty`/`wdiHarvestCattailBed_Duty`); non-code, needs a row rewrite + `/playthrough-test-plan record`.

**Framework compliance**: Tier 2, fully adopted. `ActionRouter` (31 call sites, all action interception), `SpawnService` (18), `TickEvents` (5). No direct `ActionRoutine` patches, no `DropCollectionGuardPatch`, no `ModLoaderVerison`/`ModEditorVersion`, no unfiltered hot-path prefixes. Needs CSFFModFramework 2.28.0+.

---

## Phase 0: Stabilize

> Skipped - audit score is 10/10 and there are no open retrospectives.

---

## Phase 1: Foundation

> Table-stakes maintenance. All structural hygiene is already green; these are the only open items.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Rewrite tracker row T2.601 to describe WDI's own reed/cattail harvest duties, then `/playthrough-test-plan record` the already-passed run | Tracker hygiene | P1 | Quick |
| Re-run the 6 domain sub-audits (`/full-mod-audit-chain WaterDrivenInfrastructure`) so the 2026-10-13 reports stop being carried as stale/UNTRUSTED input | Audit hygiene | P2 | Medium |
| Re-verify `GetCardCharges` is still uncached reflection; cache the `AccessTools.Field` lookup if so | Perf hygiene | P3 | Quick |

---

## Phase 2: Core Expansion

> One benched structure is the only queued content. No feature gap is open; do NOT invent new content.

### Cold-Water Cache (benched, owner decision pending)
**What**: A spoilage-halving (x0.5) river-side cache that sits strictly above vanilla's Ice Pit, with a flood-loss tick and a Pull Up Cache DA. Built in 1.12.0, removed by the owner in 1.12.1 ("Remove this as well bnch for later in ideas.").
**Why**: Completes the river-industry theme with a cold-storage tier; benched, NOT ruled out.
**Requires**: An owner decision on whether it is wanted next to the Ice Pit and whether its placeholder numbers (x0.5 spoilage, 2% daily flood loss, 4% in heavy rain) stay. Its blood-soaking quality penalty was never built (vanilla has no unbled-meat quality rule to anchor it).
**Spec / restore**: `Documentation/Design/WaterDrivenInfrastructure_Round2_As_Built.md` Feature 13; last commit holding the feature is `fb10224c1` (`git show 09be321aa:Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md` for the full as-built text).
**Complexity**: Medium (restore + re-balance + art already exists in the git history).

---

## Phase 3: Integration & Depth

### ACT Cast Iron Sheet tier-lock verification
**What**: Confirm the Cast Iron Sheet (SD4 `Active:false`) is correctly rejected from an Advanced Copper Tools copper-tier recipe slot, as the README tier-lock claim states.
**Why**: WDI consumes ACT-prefixed shared fasteners by UID and declares ACT optional (no hard dependency, G7 clean); the only open cross-mod question is this slot-rejection behavior.
**Requires**: ACT installed; a filed tracker row for the in-game check.
**Complexity**: Quick (verification, not new code).

---

## Phase 4: Polish

> Art and text are already mature (39 custom images, descriptions spot-checked clean). No open polish items.

| Item | What | Complexity |
|------|------|------------|
| (none open) | Art coverage, descriptions, and localization are all clean as of 1.12.12 | - |

---

## Long-term Vision

> Where this mod should be at v2.0.

WDI is already a complete late-game water-industry package: it covers manufacturing (sawmill, forge, workshop, grinding/stamp/fulling mills), resource extraction (ore sluice, fishpond, fish funnel), water supply (mill race outlet, cistern feed, irrigation channel) and crafting-chain depth (retting pit, cloth fulling). Its natural endpoint is a self-sufficient riverside industrial base that trains five skills and supplies water anywhere outdoors. The biggest addition not yet justified at current scope is a cold-storage tier (the benched Cold-Water Cache); beyond that the mod is feature-complete and should move to maintenance-plus-polish, updating only to track game versions and respond to requests.

**Potential major additions** (not yet justified - revisit after Phase 2/3):
- Cold-Water Cache (benched above) - the one queued content item; a riverside cold-storage tier above the Ice Pit.
- Water-powered textile depth beyond the fulling mill (e.g. a powered loom or dye vat) - only if a weaving/dyeing gap emerges that vanilla and H&F do not already cover.

Anything worth building belongs in the mod's plan under `Documentation/Plans/WaterDrivenInfrastructure/` (or its Considered table, with the reason, if not); never create a stub under `Documentation/Ideas/`, which keeps no backlog since 2026-10-01.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod WaterDrivenInfrastructure` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log`; refresh `lib/Assembly-CSharp.dll` |
| After fixing a critical issue | Run `/critical-analysis WaterDrivenInfrastructure` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo WaterDrivenInfrastructure` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod WaterDrivenInfrastructure         - full health check, updates .audit/
/critical-analysis WaterDrivenInfrastructure - adversarial review
/repair-items WaterDrivenInfrastructure      - auto-fix item JSON issues
/repair-blueprints WaterDrivenInfrastructure - auto-fix blueprint JSON issues
/build-mod WaterDrivenInfrastructure         - build Release DLL
/deploy-mods WaterDrivenInfrastructure       - build + deploy to game
/update-mod-version WaterDrivenInfrastructure <ver> - bump version in all 3 files
/export-to-repo WaterDrivenInfrastructure    - push to public repo
```

---

## Plan Reconciliation Log

> Append-only. `/roadmap` overwrites this file wholesale but re-appends this section verbatim
> as the last H2. Never reword an entry, never reorder them.

### 2026-09-07 - Audit_Remediation_Plan drained and archived (shipped as WDI 1.11.0)

**Verdict:** 2 of 2 remaining build rows shipped; 0 rows left open, so the plan is Done. Over its
life the plan carried 4 feature rows and 0 Fix rows (0 CRITICAL / 0 Design Gap throughout, at
9/10). Final state of each: **N1 Fish Funnel** built and shipped in 1.11.0; **N4 Ore Sluice
tailings** built and shipped in 1.11.0; **N3 Cast Iron Sheet sink** already shipped in 1.10.14
and now carrying a recorded in-game FAIL (T2.113) that static re-verification could not
reproduce; **N2 Fish Drying Rack** dropped 2026-08-16 by user decision (vanilla `DryingRack` +
`Smokehouse` already cover the chain). The two open Warnings (W1 em-dashes in the `ModInfo.json`
Description, W2 missing in-game caveats on the README changelog) had been fixed in an earlier
2026-09-07 pass and left uncommitted; the 1.11.0 commit carries them.

**Disposition:** ARCHIVE. Plan and pack both moved verbatim into
`Documentation/Design/WDI_Audit_Remediation_As_Built.md` (the pack as an appendix, because its
Prompt 1 CONFIRMED BUILD DIRECTIVE is the only record of the decompile research behind the
shipped cards, and its Wave-0 block holds the vanilla `DryingRack`/`Smokehouse` GUIDs the N2
research produced). `Documentation/Plans/WaterDrivenInfrastructure/` is now empty and removed.
3 acceptance rows filed before anything was deleted: **T2.198** (funnel builds and places),
**T2.199** (funnel actually doubles a Funnel Trap's fill rate), **T2.200** (sluice tailings).
Nothing was pruned on a classification alone - every row below was re-read from source.

**Evidence:** `dotnet build -c Release` 0 warnings / 0 errors;
`Development_Tools/Deploy-Mods.ps1 -WaterDrivenInfrastructure` reports CardData files 61 -> 64.
Card census over `CardData/**/*.json` after the change: CT0 24, CT2 11, CT7 25, CT10 4.
`BlueprintTabs.json` Advanced Tools 5 -> 6 entries, 25 total across all tabs, no duplicate UID.
`Localization/SimpEn.csv` and `SimpCn.csv` both 611 -> 626 rows (15 keys, appended in binary
mode with a byte-prefix assertion, so no existing row was rewritten). A purpose-built checker
verified 32 `*WarpData`/`*WarpType` pairs across the 3 new cards and was watched going RED on 8
separately planted defects (missing WarpType, CT10 instead of CT2, inactive Special1 modifier,
(0,0) drop quantity, `BuildingDaytimeCost` 16, an obfuscated vanilla asset name, an empty
`EffectName`, and a DA with no effect) and GREEN again after each was reverted.
N4's honesty gate could NOT be met as literally written and was not faked: a sweep of all 2841
entries in `UniqueIDScriptableGUID/CardData.json` found no Gravel, Sand, Pebble, Rubble or
aggregate card in EA 0.67i, so the tailings route to real vanilla Stone, and the row's premise
was narrowed from "discards its waste silently" to "about 29% of mud piles and 34% of fine dirt
yielded nothing at all" (computed from the per-soil drop chances in `RollSluiceDrops`).
`.claude/playthrough-test-status.json` items 110 -> 113, `confirmedItems` untouched at 229,
conservation-checked line-by-line with a multiset compare against the HEAD blob.

### 2026-09-08 - Fleet plans consolidated into Master_Plan (Master_Plan, Duties_Ownership_Plan, Playthrough_R33_Failure_Remediation_Plan)
- **Scope:** Fleet. This same entry is recorded in the ROADMAP of CSFFModFramework, Community_Mod_Chest,
  Sirus23_Mod_Collection, WaterDrivenInfrastructure and AdvancedCopperTools. PartnerOverhaul, named by the
  R33 plan for T3.39, has no ROADMAP.md, so its rows are carried by the tracker and by
  `Documentation/Plans/Fleet/Master_Plan.md` only.
- **Verdict:** Master_Plan (2026-08-16 revision): 3 of 3 open items closed in-game (T2.63 and T2.77 PASS r33;
  T2.75 and T2.68 PASS r31); its 3-prompt pack is 3 of 3 VERIFIED-DONE; 1 loose end (fleet legacy-card
  GUID scan) still unactioned and carried. Duties_Ownership_Plan: M0, M0b, M2 and 4 of 5 M1 checks
  confirmed; M3 2 of 4 stations PASS (T2.87 Ore Sluice, T2.88 Sawmill) and 2 FAIL-confounded (T2.89 Forge,
  T2.90 Workshop); M3.5 root cause A fixed in WDI 1.10.13 and validated by r33 (three station duties ran),
  root cause B still confounded with an unbuilt or cold station; the 5th M1 check (ACT Brazier/Lantern
  ownership rows) had no tracker row and was backfilled as T2.212; M4 Fishpond is an owner decision with no
  sequencing intent; the pack's T1.56 ActionRouter note is still an open framework defect. 0 code rows
  remain in the Duties plan itself. Playthrough_R33_Failure_Remediation_Plan (14 rows, written 2026-09-06):
  re-derived today; 2 rows dissolved (T3.39 precondition unmet, T1.71 mis-scoped), 1 certain code fix still
  unwritten (CopperChestPatch GetSlotForCard arity, T4.40 and T4.38), 1 gated on a tester answer (T2.99, now
  behind the T2.207 vanilla discriminator), 1 design decision (T2.101), 6 human-gated re-runs, 2 needing a
  symptom; of its 5 Wave-0 actions 3 have since landed (Disk LogLevels includes Debug, VerboseLogging =
  true, CMC 1.68.19 once-per-change hearth verdict logging) and 2 remain (T3.39 re-arm, T1.71 re-scope).
- **Disposition:** Master_Plan KEEP, rewritten in place as the single fleet plan with a regenerated pack (6
  prompts, 1 open decision). Duties_Ownership_Plan ARCHIVE to
  `Documentation/Design/Duties_Ownership_As_Built.md` (plan verbatim, pack appended as an appendix) because
  the Blast/bellows automation rejection, the one-duty-per-card finding and the M3.5 diagnosis exist
  nowhere else; its pack DELETED from Plans; M4 moved to
  `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md` carrying its reason.
  Playthrough_R33_Failure_Remediation_Plan DELETE, folded into Master_Plan (per-row evidence already lives
  verbatim in the tracker notes; last full text at commit f4bc0f9d5); the T2.101 design question moved to
  `Documentation/Ideas/Sirus23_Mod_Collection/SHEEP_PEN.md` with its reasoning.
- **Pruned:** Master pack Prompts 1-3 and the plan's three Open Work sections (all PASS); Duties sections M0,
  M0b, M1 (4 of 5 checks), M2, M3 Ore Sluice and Sawmill, M3.5 root cause A; R33 Class A rows as defects,
  the Class D instrumentation asks now shipped in CMC 1.68.19, and the two Wave-0 config preconditions now
  armed in the deployed install.
- **Evidence:** tracker read by id across items/confirmedItems/droppedItems (124/229/20 rows at read time):
  T2.63, T2.77, T2.87, T2.88 carry `r33 PASS`; T2.75, T2.68 carry `PASS via r31`; T2.89, T2.90, T2.99,
  T2.101, T2.149, T2.137, T2.147, T2.128, T1.51, T2.113, T3.39, T1.71, T4.40, T4.38 all read `fail`;
  T1.56's note still reads `OPEN FRAMEWORK DEFECT - NOT YET FIXED`. Code re-read, not inherited:
  `Community_Mod_Chest/Patcher/CopperChestPatch.cs` still invokes `GetSlotForCard` with 4 arguments while
  `.decomp/GraphicsManager.cs:2587` declares 5; `MillDutyPatch.StationDutyBaseWeight = 850`, and all five
  station UID constants match their CardData `UniqueID`s; the forge and workshop `CompatibleDutiesWarpData`
  arrays carry both the Firekeeping GUID and their station duty UID; `CSFFModFramework/Api/ActionRouter.cs`
  still resolves `_cocReceiverIdx` as the first `InGameCardBase` parameter;
  `Sirus23_Mod_Collection/GameSourceModify/VanillaFox_Agent1.json` `TriggerCardsWarpData` still holds only
  the three dried-berry GUIDs; `SheepPenPatch.cs` still returns before the roll when a wolf is present;
  `VillageFireplacePatch.LogVerdict` (Info, once per change) is present, added in 1.68.19; all four ACT
  Brazier/Lantern CardData halves carry `UsesOwnershipSystem: true`. Deployed config read directly:
  `BepInEx.cfg` `[Logging.Disk] LogLevels` includes Debug, `crispywhips.CSFFModFramework.cfg`
  `VerboseLogging = true`, `crispywhips.partner_overhaul.cfg` `Reserved Fuel/Wood UIDs` empty.
  `ls */.audit/legacy-card-refs*` returned nothing (control: the same shell listed 13 `.audit/` folders).
  Citation sweep: 3 live pointers repointed (`.claude/commands/plan-to-prompts.md`,
  `.claude/commands/audit-plan.md`, `Playthrough_Test/README.md`) plus 3 memory files; two dated records
  (`CSFFModFramework/CHANGELOG.md` [2.22.5] and the `MillDutyPatch.cs` doc comment) left as written, and
  the archive's header names the old path so a grep for it still lands.
- **Verification debt:** T2.89, T2.90, T2.212 (new), T2.207 then T2.99, T2.101, T2.149, T2.137, T2.147,
  T2.128, T1.51, T2.113, T3.39, T1.71, T4.40, T4.38, all present in `.claude/playthrough-test-status.json`.

### 2026-09-09 - Fleet Master_Plan (Scope: Fleet)
- **Scope:** Fleet. The same entry is recorded in AdvancedCopperTools, CSFFModFramework,
  Community_Mod_Chest, HerbsAndFungi, Sirus23_Mod_Collection and WaterDrivenInfrastructure
  (PartnerOverhaul, also named by the plan, has no ROADMAP.md).
- **Verdict:** 0 of the 7 prompts and 1 decision carried by the 2026-09-08 rewrite remain as code.
  Prompts 1-4 BUILT, awaiting in-game verification (CMC 1.68.22, Sirus23 1.21.1, PartnerOverhaul
  1.0.4 + tracker re-arm, CSFFModFramework 2.25.29); Prompts 5 and 6 verified done 2026-09-09;
  Prompt 7 REFUTED (its premise was false, nothing built); the OPEN-DECISION (T2.101) taken as
  delegated and shipped as Sirus23 1.21.2 (partial wolf guard). 5 items awaiting verification, all
  with tracker rows.
- **Disposition:** ARCHIVE. Plan moved to `Documentation/Design/Fleet_Master_Plan_As_Built.md`
  with the refreshed pack appended as an appendix; the pack file deleted from
  `Documentation/Plans/Fleet/`. Inbound citations (tracker row sources, two earlier As_Built notes,
  three memory files) repointed or annotated in the same commit.
- **Pruned:** section 1.6 / Prompt 7 (closed as refuted, recorded in
  `Community_Mod_Chest/.audit/legacy-card-refs-2026-09-08.md` Correction 2); section 2.1 /
  OPEN-DECISION (decided; reasoning kept in
  `Documentation/Ideas/Sirus23_Mod_Collection/SHEEP_PEN.md`). Sections 1.1-1.4 were already
  collapsed to awaiting-verification pointers by the 2026-09-09 refresh and are unchanged.
- **Evidence:** Prompt 7: `git blame -L369,369 -- Community_Mod_Chest/TradingValues.json` returns
  `fa3dffd6f` (2026-07-27) for the row pricing `85f90db88eaa1804186ea7c3e561dd92` at 1500;
  `git log --oneline -- Community_Mod_Chest/TradingValues.json` returns that single commit; the
  export's `CardData/LeatherGloves.json` re-read as UniqueID `85f90db88eaa1804186ea7c3e561dd92`,
  `TradingValue` 0.0 (so the listed row is what prices it); grep for that GUID across every
  `TradingValues.json` and `GameSourceModify/` file returns only the CMC row (no later override).
  T2.101: r33 log quoted in the tracker row (five nights of `stood guard`, zero losses, 7/7/7/7/3
  unpenned); `SheepPenPatch.cs` early return replaced by `GuardedPredationChance` 0.01f; Release
  build 0 warnings / 0 errors; `Deploy-Mods.ps1 -Sirus23ModCollection` deployed 84 CardData, DLL
  byte-identical to bin/Release, the new literal `wolf on guard` present in the DLL's #US heap
  (UTF-16-LE); zip `Sirus23_Mod_Collection_1-21-2.zip`. Pack header at archive: 0 open, 5
  awaiting verification.
- **Verification debt:** T4.40, T4.38 (CMC Copper Chest); T2.207 then T2.99 (Sirus23 fox bait);
  T3.39, T1.71, T2.147, T2.128 (tracker hygiene, PartnerOverhaul count line, clone-tile live
  trim); T1.56 via T1.81 (framework card-on-card dispatch); T2.101 and T2.232 (Sheep Pen, without
  and with a wolf); plus the section 3 rows T2.89, T2.90, T2.212, T2.149, T2.137, T1.51, T2.113.
  All present in `.claude/playthrough-test-status.json`.

### 2026-09-26 - Audit_Remediation_Plan (re-promoted 2026-09-22) drained and deleted
- **Verdict:** 1 of 1 fix rows done and shipped; 0 feature rows (none were promoted). F1 (code-quality
  B1: `ex.Message` in the two Debug-level Invoke catches `RefreshCardDurabilityVisuals` and
  `RefreshOpenInventoryPopup` in `Patcher/ActionInterceptPatch.cs`) was fixed in source by `ac8406c65`
  on 2026-09-22, the same commit that created the plan, with no version bump. It reached players in
  1.11.3 (`9dddf47dd`, 2026-09-25). The plan stayed open only because it was waiting on that bump.
- **Disposition:** DELETE. Nothing in it is the sole record of anything. The F1 fix and its reasoning
  live in the `ac8406c65` commit message and this entry. The five Medium-Term candidates its Features
  section named (Water Wheel power model, structure wear loop, Fishpond feeding, Fishpond Partner
  fishing duty, Water Reservoir) stay in `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md`. The
  Warnings its Promotion Log left unpromoted (G5, F4/W4, I2-adjacent, nstrip/T1.71, stale EA-version
  string) are all Phase 0/1 rows above. No acceptance criterion needs a tracker row, because a Debug
  log-text change has nothing a player can observe. There was no Implementation Prompts pack. Last
  full text: `git show ac8406c65:Documentation/Plans/WaterDrivenInfrastructure/Audit_Remediation_Plan.md`.
- **Pruned:** the whole plan (F1, the Features "none promoted" note, the Promotion Log). Phase 1's
  [B1] row moved to "Resolved since the last roadmap" above, and the Stability line now says B1 shipped.
- **Evidence:** `ActionInterceptPatch.cs:2083` and `:2618` read `ex.InnerException?.ToString() ??
  ex.ToString()` on disk. `git merge-base --is-ancestor ac8406c65 9dddf47dd` is true. `ilspycmd` on the
  committed 1.11.3 `bin/Release/WaterDrivenInfrastructure.dll` blob at `9dddf47dd` (the commit
  `.claude/mod-publish-status.json` records as published; Nexus carries 1.11.3) shows both catches
  unwrapping, while the same decompile of the pre-fix blob at `ac8406c65~1` shows `ex.Message` at both,
  so the probe can tell the two apart. The row's scope claim ("every functional Invoke site in the
  mod already unwraps correctly") was re-checked rather than inherited: a try/catch scan of all 10 WDI
  `.cs` files found 47 `ex.Message` catches without an unwrap and 0 whose try body calls `.Invoke(`,
  and the same scan over the pre-fix source found exactly the 2 F1 sites (control). Citation sweep:
  the live pointers were this ROADMAP's Phase 1 row and the "not yet shipped" resolution lines in the
  untracked `.audit/summary.md` and `.audit/code-quality.md` (a shipped line appended to each).
  `Documentation/Fleet_Review_Queue.md` (42nd-pass narrative and row 307) and two memory files cite
  the path as dated records and were left as written.
- **Verification debt:** none from this plan.

### 2026-09-28 - Content_Update_Plan (WDI 1.12.0 Waterside Homestead) built and archived
- **Verdict:** 4 of 4 selected features built on the plan's defaults and versioned 1.12.0: Mill-Race
  Cistern Feed, Fishpond Feeding, Reed and Cattail Beds, Millwright's Craft skill XP. 0 build rows open.
  Open questions 1 (irrigation) and 2 (Partner fishing duty) were never in scope and stay in IDEAS.md.
  12 acceptance checks await verification, all filed; 7 are harness work, 5 need a person.
- **Disposition:** ARCHIVE. The plan moved verbatim, plus an "As built" section, to
  `Documentation/Design/WaterDrivenInfrastructure_Content_Update_1_12_0_As_Built.md`;
  `Documentation/Plans/WaterDrivenInfrastructure/` is empty and removed. The 12 tracker rows and their
  checklist entries point at the archive; the IDEAS.md header names it beside its dated plan citation.
- **Pruned:** nothing. The TestHarness scenarios its Acceptance blocks name were not written; they are
  carried by the `[harness]` rows below ("needs a scenario written, then a run").
- **Evidence:** `WaterDrivenInfrastructure.Tests.ps1` 11/11 with 6 new self-demonstrating contracts;
  189/189 across the neighbouring card, blueprint, localization, art and stat gates;
  `Check-LocalizationParity.ps1` CLEAN 472/472; `dotnet build -c Release` 0/0; `Audit-Mod-Preflight.ps1`
  0 critical, 0 warning, versions 1.12.0 in all three places. Card census after the change: CT0 24, CT2 13,
  CT7 27, CT10 4; `BlueprintTabs.json` 27 entries. Live `GameTechInfo.HotfixNumber` read as 2 (EA 0.68b).
- **Verification debt:** T2.380-T2.383 (cistern feed), T2.384-T2.385 (feeding), T2.386-T2.389 (beds),
  T2.390-T2.391 (skill XP), all in `.claude/playthrough-test-status.json`.

### 2026-10-04 - MASTER_PLAN

- **Verdict:** counts: 16 feature sections checked (3, 9, 10, 11, 12, 13, 16, 17, 18, 20, 21, 22, 23, 24, 25, 26) and 17 pack prompts. 0 features newly verified done; 3 built (feature 9 awaiting T2.606, feature 24 awaiting the person's look at the art in T2.609, feature 10 built but T2.608 LEG C failed twice); 13 open and still unbuilt. Prompts: 1 verified done and removed (Prompt 2, scenario repairs landed in a6bf2e1f8), 16 open (was 17). One stale claim corrected: WDI source moved after 5829060cc (cb8d632d7, the Reed Bed and Cattail Bed card art).
- **Disposition:** KEEP IN PLACE (open work remains: 16 open prompts).
- **Pruned:** pack Prompt 2 (cistern amounts T2.380-T2.383 and the T2.457 reload route), its "failed, fix pass open" bullet (moved to the Awaiting verification list), and the plan's "Remaining implementation" row for it; no plan feature section was deleted (none newly done). Plan and pack wording about Prompt 2, the game's deployed WDI commit and T2.609's record step was corrected, and a dated status paragraph appended to the plan.
- **Evidence:** `git grep -il -E` over `WaterDrivenInfrastructure/` (excluding bin and obj) for the unbuilt features' markers returned 0 files each (Closed outlet or Close Valve, `tag_x_WaterSource`, `copper_bathtub_empty_placed`, `coldwater_cache`, `Spearfish`, `bronze_gear`, `stamp_mill` or `Stamp All`, `wdiHarvest`, `wdiFishPond`, `_Expanded`, `CardGif`; `overshot` and `wdi_action_batch` hit only `ROADMAP.md`), and the same probe form returned 10 files for `water_basin`, 3 for `HasHeatSourceAt`, 8 for the Workshop iron crafts, 7 for `Scatter Fish Food` and 2 for `WdiOutletFeedsCistern` (the control that the probe can hit); `ls-files` shows no `Resource/GIF`, `CardData/Gif` or `ScriptableObject` folder and five NPCDuty files (no sixth, no harvest or pond duty). Code claims re-read: `InvokeSpendDaytimePoints` 0 hits in `Patcher` (helper removed as the pack says; the same loop returned non-zero for 19 other symbols), `MillGrindAll` and `SluiceAll` still `ActionTiming.Cancel` in `ActionInterceptPatch.cs`, Sawmill 6 slots and 20000 weight, Workshop 14 slots, Grinding Mill 18 slots and 8000 weight, all non-mini. `git diff --stat 5829060cc HEAD -- WaterDrivenInfrastructure` (bin and obj excluded) lists 7 files, all the bed art; `bin/Release/WaterDrivenInfrastructure.dll` md5 starts 1863985e as the pack recorded. `git show a6bf2e1f8` read for T2.380-T2.383 and T2.457 (expectations netted for evaporation, `session/clone` slot 9 to 10); the scenarios were NOT re-validated by `--dry-run` here (no harness in this pass) and the evaporation figure was not re-derived from the export. Tracker: every T-id left in the plan (26) and pack (22 after the edit) resolves to a row in `.claude/playthrough-test-status.json` (892 rows scanned by script, 0 missing); T2.80, T2.87 and T2.88 sit in `confirmedItems`, the rest in `items`. T2.435's scenario file still scripts only the Sawmill Cut leg (its three open legs appear only in its `covers` text), so Prompt 3 stands.
- **Verification debt:** T2.606 (feature 9 without ACT); T2.608 (feature 10, LEG C failed); T2.609 (art look); T2.380-T2.383 and T2.457 (repaired, rerun owed); T2.384-T2.391 (never run); T2.435 (three legs unscripted); T2.89 and T2.90 (fail, feature 3); T2.198 and T2.199 (feature 20); T2.601 (dated fail, re-armed by a new row in Prompt 14).

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** 15 plan claims re-checked (features 3, 11, 12, 13, 16, 17, 18, 21, 23, 24, 25, release tail 14, the four-image art gap, and the 1.12.0 harness-lane rows) and 6 pack prompts. VERIFIED-DONE pruned: 0 new features (feature 16's long section collapsed, not deleted), plus already-done leftovers removed: Selected rows 9, 22 and 26, Wave 0 row FW3, the beds' Wave 0 findings bullet, 5 risk rows (features 9 and 22, the flood-stat roll after T2.618, the action-time lever after T2.620). BUILT awaiting verification: 7 features (3, 13, 16, 17, 21, 24, 25) plus the harness-lane rows, 9 pack items. OPEN: 6 prompts (19 art, 9 bronze gears, 15 pond duty, 16 Expand, 13 GIF, 17 closeout), unchanged. FAILED (harness, recorded or not): T2.384, T2.387, T2.388, T2.617, T2.619, T2.623, T2.624. OBSOLETE: 0. STALE-CLAIM corrected: 2 (the deployed WDI build is 06be9f74f, md5 be47ee3a, deployed 2026-10-06 12:39, not the 2026-10-05 merge build; feature 16 was built while its section still carried full Build prose). Pack reordered so Prompt 19 is first, matching the plan's dispatch order.
- **Disposition:** KEEP IN PLACE - 6 open rows of code work remain.
- **Pruned:** plan: feature 16's Player-facing/Grounding/Build/Rules/Acceptance/Risk/Done/Depends prose (collapsed to a BUILT pointer, text at `a11a1fd6f`), Selected rows 9, 22, 26, Wave 0 row FW3, the beds' Wave 0 findings bullet, five risk rows, Open question 6 to one line, and the four dated status paragraphs condensed into one history paragraph (they resolve at `a11a1fd6f`). Pack: no prompt removed; the Awaiting verification list moved under a real `## Awaiting verification` heading with one compact line per item, and the 2026-10-07 harness readings folded in.
- **Evidence:** `Grep` over `WaterDrivenInfrastructure/` (bin and obj excluded): `tag_x_WaterSource` hits `Patcher/MillRaceNetwork.cs` line 425 and bathtub code sits in `Patcher/BathtubPlumbingPatch.cs` (TransformCardInPlace then SetDurability, ActCompat-gated), the control that the probe hits; `bronze_gear` 1 file (`ROADMAP.md` only); no `*.gif`, no `CardData/Gif`, no `*Expanded*` file; `NPCDuty/` holds 8 files, none a pond duty; `Resource/Picture/` has no ColdWaterCache, OvershotWheel, StampMill or StampMillKit PNG while 7 card JSONs name them. `git log 72f04b7eb..HEAD` on WDI source: no commits after 06be9f74f. Deployed `WaterDrivenInfrastructure.dll` md5 be47ee3a equals `bin/Release`. Tracker: all 30 plan and 26 pack T-ids resolve (1001 rows scanned). Harness reports dated 2026-10-07 under `TestHarness/client/reports/`: PASS for T2.90, T2.198, T2.609, T2.621, T2.622; FAIL for T2.384 (step 78), T2.387 (66), T2.388 (18 steps), T2.617 (20), T2.619 (24), T2.623 (36, only its cost "2" expectation: the Partner's Stamp All started at cost 0), T2.624 (35, 37: a Heated Copper Nugget, no Unfinished Metal Bar). None of these is recorded in the tracker yet.
- **Verification debt:** T2.90, T2.609, T2.631 (scenario still to write), T2.617, T2.619, T2.621, T2.622, T2.623, T2.384, T2.387, T2.388, T2.390, T2.391, T2.198, T2.624.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** 14 plan claims re-checked (features 3, 11, 12, 13, 16, 17, 18, 21, 23, 24, 25, release tail 14, the four-image art gap, and the 1.12.0 harness-lane rows) and 6 pack prompts. VERIFIED-DONE: 1 (feature 3, T2.89 and T2.90 CONFIRMED). BUILT awaiting verification: 4 features (13, 21, 24, 25) plus the harness-lane rows, 6 pack items. FAILED (harness, unrecorded, not yet triaged scenario vs code): 3 (feature 17 T2.619, feature 16 T2.631, T2.624). OPEN: 6 prompts (19 art, 9 bronze gears, 15 pond duty, 16 Expand, 13 GIF, 17 closeout), unchanged and still in that order; Prompt 19 is the next dispatch (no deps, art only, closes the Sprite-NameResolves gap). OBSOLETE: 0. STALE-CLAIM corrected: 2 (T2.631's scenario is written, 48b0a0c1c, and fails, where the plan said "scenario still to write"; T2.623 no longer fails on its cost expectation but its PASS was rejected by a skeptic).
- **Disposition:** KEEP IN PLACE - 6 open prompts of code and art work remain.
- **Pruned:** plan: section 3 (Forge and Workshop Partner duties; text at `cc21d71ee`), its Selected row, its risk-register row and its Phase A label; the one load-bearing fact (a Partner-performed DismantleAction reaches its `AfterWrapped` handler, T2.90 step 44) moved into section 12's Depends on. Pointers for features 13, 16, 17, 21, 24 and 25 re-worded to the 2026-10-09 tracker and harness state, and a dated status paragraph appended. Pack: the feature 3 Awaiting bullet removed (T2.90 CONFIRMED in 51641ef64); feature 16, feature 17 and T2.624 moved to a new "Failed - needs a fix pass" list; Prompt 17 steps 2 and 3 updated (README's Workshop-duty caveat can go now). No prompt removed. Nothing committed by this pass.
- **Evidence:** `git log a11a1fd6f..HEAD -- WaterDrivenInfrastructure` returned 3 commits, none touching source: 850b3dc9d (ROADMAP), e57d44cfc (the committed `bin/Release` DLL) and 2d9e0cccc (`.art/reference-uploads.json`); `git log 06be9f74f..HEAD` likewise. Deployed `WaterDrivenInfrastructure.dll` md5 be47ee3a equals `bin/Release` (both 2026-10-06 12:39). Unbuilt features re-probed: `NPCDuty/` holds 8 files, none a pond duty; a grep for `bronze_gear`, `_Expanded` and `wdiFishPond` over WDI JSON and C# returned 0 files, and no `*.gif` under any `Resource/`; control: 7 `CardImageWarpData` lines name the four missing sprites while `Resource/Picture/` holds 0 of those PNGs. Tracker read by script (WDI rows across items, confirmedItems and droppedItems): T2.89, T2.90, T2.609, T2.618, T2.620 in `confirmedItems`; T2.198, T2.384, T2.387, T2.388, T2.390, T2.391, T2.617, T2.619, T2.621, T2.622, T2.623, T2.624, T2.631, T2.1384, T2.1385 pending. Harness `report.md` verdicts under `TestHarness/client/reports/`, newest per row: PASS on 2026-10-09 for T2.198, T2.384, T2.387 (after a FAIL the same morning), T2.388, T2.390, T2.391, T2.617, T2.621, T2.622, T2.623; FAIL for T2.619 (step 36, Sturgeon Quality 63 against 72), T2.624 (steps 35 and 37) and T2.631 (step 29). T2.621 and T2.623's PASSes are on the coordinator's list of 40 rejected by a skeptic in the 51641ef64 review, so they are not counted as done. Citation sweep for the deleted section 3 over `Documentation/`, the tracker, `Playthrough_Test/`, the WDI scenarios, the mod folder and the memory directory: only in-plan references, all rewritten.
- **Verification debt:** T2.1385, T2.617, T2.621, T2.622, T2.623, T2.619, T2.631, T2.624, T2.198, T2.1384, T2.384, T2.387, T2.388, T2.390, T2.391.

### 2026-10-09 - MASTER_PLAN feature 13 removed by owner decision (WDI 1.12.1)
- **Verdict:** 1 feature removed from the plan and the mod (feature 13, the Cold-Water Cache), not done and not ruled out: the owner said "Remove this as well bnch for later in ideas." 0 other features changed.
- **Disposition:** KEEP IN PLACE - the plan still holds open code and art prompts (pack Prompts 19, 9, 15, 16, 13, 17); feature 13 is benched in `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md`.
- **Pruned:** plan: section 13 (reduced to a removal pointer), its Selected row, its art-table item, its risk row, Open question 12 (moot), its Wave 0 findings bullet and the Considered row for the undrained-blood loss (all moved to the Ideas file; text at `fb10224c1`). Pack: its Awaiting verification item, ColdWaterCache in Prompt 19, and its docs-list mention. Mod: both cache cards, `Patcher/ColdWaterCachePatch.cs`, its wiring, nine CSV rows per language, README section, Features.json group, WDI Pester contract, two harness scenarios.
- **Evidence:** a case-insensitive grep for `coldwater`, `cold-water` and `ColdWaterCache` over `WaterDrivenInfrastructure/` (bin, obj excluded) leaves only CHANGELOG history and earlier entries of this log; a Release build of WDI succeeds; save behaviour read from `.decomp/GameManager.cs` (`LoadCard` returns false for an unresolved UniqueID, so the cache and its contents are not loaded).
- **Verification debt:** none added; the cache's tracker rows are retired with it.

### 2026-10-09 (later) - MASTER_PLAN, second /cleanup-plans pass
- **Verdict:** NOT drained. 7 open prompts (19 art, 20 new T2.624 fix, 9, 15, 16, 13, 17); features 11, 12, 18, 23 unbuilt; 4 built awaiting verification (17, 21, 24, 25); feature 16 built but blocked on ACT (T2.631); feature 13 removed by the owner.
- **Disposition:** KEEP IN PLACE - code work remains.
- **Pruned:** no sections; 7 stale claims corrected (version 1.12.1, no ACT plugin dependency since e90668165, ActCompat MetalPartsPresent/IsInstalled split 76aac3d8a, act_tin_ore entries removed c6b2bad35, new Overshot Wheel recipe and withdrawn Partner time claim e0fd3ad36); the pack's Failed section replaced by "Blocked - on another mod"; new Prompt 20 for GameLoadPatch.InjectGreenstonePowderSmeltRecipe overwriting the Smelt Greenstone drop with a Metal Nugget.
- **Evidence:** audited against 1d9cbf121. Grep finds no .gif/CardData/Gif, no pond duty among the eight NPCDuty files, no _Expanded card and no bronze_gear (four open features unbuilt); OvershotWheel, StampMill and StampMillKit PNGs absent; deployed DLL md5 equals bin/Release. Gates Plans-AwaitingRowIds 5/5, Plans-Policy 12/12, PlaythroughRow-Citations 13/13, PlanReconciliation-Record 13/13.
- **Verification debt:** T2.619, T2.621, T2.623, T2.624 (reruns); T2.631 (blocked on the ACT T2.785 fix); T2.388 (PASS, FIX-SCENARIO at the 2026-10-09 second review); T2.391 (newest run FAIL); T2.198, T2.384, T2.387, T2.390 and T2.622 recorded CONFIRMED the same day. A tracker row for the rebuilt Overshot Wheel recipe is owed (pack Prompt 17).

### 2026-10-10 - MASTER_PLAN drained and archived (/cleanup-plans apply)
- **Verdict:** DRAINED. 0 open prompts (pack Prompts 1 to 22 all done; 21 was a scenario-fault triage, 17 the release closeout, 22 the empty-station duty gate). Every feature section is built: 11 (GIF), 12 (Partner pond duty), 16 (water-source tag and bathtub, blocked only on ACT's own `tag_WaterAny` data), 17 (rod and spear), 18 (Expand), 21 (Overshot Wheel), 23 (bronze gears), 24 (Water Basin), 25 (Stamp Mill); feature 13 removed by the owner; Open questions 2 and 3 are owner decisions with no code depending on them. The 2026-10-10 pass also fixed two WDI bugs the reruns exposed (pond catch gates at a hundredth of a fish; PondRodSpearCatch wiping FishCatchStats' shared ctx.Tag so no pond Catch stamped Quality 72; f1ba06d15) and an unquoted CSV comma (6cef1b325).
- **Disposition:** ARCHIVE to `Documentation/Design/WaterDrivenInfrastructure_Round2_As_Built.md` (the Considered table with its rejection reasons, the Wave 0 findings and the open owner questions are design detail worth keeping); the pack is deleted, its final Awaiting verification, Blocked and Not-WDI sections folded into the archive as an appendix. WDI 1.12.2 is unpublished; the mod is now a `Stable/` candidate (`Stable/README.md`, move with `Development_Tools/Move-ModFolder.ps1`).
- **Pruned:** the whole plan and pack left `Documentation/Plans/`; 61 inbound citations across 32 files repointed to the archive (dated records left as they were: earlier entries of this log and `Documentation/Reports/Plan_Review/plan-review.html`).
- **Evidence:** existence probe over `WaterDrivenInfrastructure/` with a control (`CardData/Location/Fishpond_Filled.json`: 1 hit): GIF 2 files, pond duty references 4, `BathtubPlumbingPatch.cs` 1, PondRodSpear handlers 2, Expanded cards 2, Overshot cards 2, bronze gear cards 4, Water Basin cards 2, Stamp Mill cards 3, the Prompt 22 postfix 3 lines. Harness on WDI 1.12.2 (rigs 12 to 16, committed source): T2.388, T2.391, T2.619, T2.621, T2.623, T2.624, T2.633, T2.634, T2.635, T2.636, T2.637 and T2.638 PASS on 2026-10-10 (`autoplay.py audit` read all twelve FULL against 6cef1b325; the final rerun against 9f286d129 is cited in each row's note). WDI Release build 0 warnings; WDI Pester contracts green except 'Winter heat sources', red only after the non-WDI commits 409b61f25, d8db146f1 and 29dd4001b.
- **Verification debt:** T2.388, T2.391, T2.619, T2.621, T2.623, T2.624, T2.633, T2.634, T2.635, T2.636, T2.637, T2.638 (harness PASS, awaiting `autoplay.py record --apply`, blocked on 2026-10-10 by other sessions' uncommitted tracker edits); T2.639, T2.1384, T2.1385 (a person's look at art); T2.631 (blocked on ACT's bathtub data, then rerun).

### 2026-10-11 - Fleet MASTER_PLAN (cross-mod decoupling), /cleanup-plans Fleet apply
- Scope: Fleet (cross-mod plan; the same entry is recorded in the ROADMAP of every mod it names: Community_Mod_Chest, AdvancedCopperTools, WaterDrivenInfrastructure, HomesteadPerks, HerbsAndFungi, Sirus23_Mod_Collection, CSFFModFramework).
- **Verdict:** DRAINED. Features 1-13 all built (1-3, 6, 7, 9, 12 in the commits `33ab18fe9`, `a3f965d43`, `76aac3d8a`, `cc0115b9e`, `fed7f7d0a`, `c6b2bad35`, `31034e212`, `e7b558ead`, `7219cd11d`), 0 open pack prompts, 0 unbuilt rows; every acceptance row recorded CONFIRMED (T2.789, T2.632, T2.997, T2.999, T2.998, T2.549, T2.557, T2.560, T2.1236 to T2.1242). Open question 4 settled by measurement, not by an owner choice.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Fleet_Cross_Mod_Decoupling_As_Built.md` with its Considered table and its drained prompt pack appended; the pack was deleted.
- **Pruned:** plan sections 1, 2, 3, 6, 7, 9 and 12 (each BUILT and confirmed), the stale "awaiting verification" pointers, open question 4's default text (replaced by its measured outcome); pack Prompts 9 and 10 (Prompt 10 had already been built in `e7b558ead`, the pack was stale on that point).
- **Evidence:** on dev lane rig 6 at HEAD (H&F, Harsh Wilds and Sirus23 deployed from the working tree, the rest synced from the real install), harness PASS for T2.557, T2.560, T2.632, T2.997 (both variants), T2.999 and T2.1236 to T2.1242; two independent skeptic reviews of all twelve rows (`.claude/handoffs/skeptic-review-2026-10-11-fleet/r-a.md`, `r-b.md`): 11 RECORD-PASS, and T2.560 FIX-SCENARIO (its brine leg dragged the non-draggable Liquid sub-card, so it passed on any build; fixed in the scenario and rerun PASS with a real refusal, "Dropping 'Pickle Brine' onto 'Wolf Companion' does nothing", beside the same container type with water running Give Water once). Three of the seven absent-partner pins had drifted since 2026-10-09 and were re-measured from real alone-boots (CMC 48 entries, five sprites now resolve; H&F plus twelve of its own map-node forward references; Harsh Wilds plus the Forest Lore `fl_mud_volcano` weight row; WDI now 0 lines), each entry checked against `Documentation/Interop_Registry.md`. End state: `grep BepInDependency(` over every `Plugin.cs` finds 13 files and none names anything but CSFFModFramework (the 13 matches are the control that the scan can hit). Open question 4 probe: with Sirus23 set aside the Wool-Quilted Cap and Vest each keep four requirement slots and the Wool one is null (measured), and a researched blueprint is left alone by `AcademyCourseService.HideWoolQuiltedIfNoWool` (read from code).
- **Verification debt:** none open. One unplayed fact, deliberately without a row: what a craft of an already-researched Wool-Quilted recipe asks for after Sirus23 is removed (the game reads a null slot as no requirement, the fleet audit's reading of the code); CMC's README states it as unplayed. Releases still owed per mod under its own version rules (`.claude/nexus-publish-status.json` against `ModInfo.json`, read 2026-10-11): framework 2.28.0 (Nexus holds 2.26.12), CMC 1.69.6 (1.68.45), ACT 1.18.1 (1.16.10), WDI 1.12.2 (1.11.3), HSP 1.3.1 (1.2.4), H&F 1.14.1 (1.13.7) and Sirus23 1.22.0 (1.21.6); MUM not repacked for this change.

### 2026-10-13 - Ideas exception closed: Cold-Water Cache moved out of Documentation/Ideas
- **Verdict:** 1 benched idea (the Cold-Water Cache, Round 2 plan feature 13, built in 1.12.0 and removed in 1.12.1) was the only entry in `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md`. It is benched, not ruled out: owner 2026-10-09, verbatim, "Remove this as well bnch for later in ideas." 0 code rows; WDI has no plan folder.
- **Disposition:** DELETE the IDEAS.md (the folder held nothing else) and record the idea in this ROADMAP's "Considered, not selected" table with its reason, per root CLAUDE.md "Plans folder" (no backlog under `Documentation/Ideas/` since 2026-10-01). Re-plan it into a new `Documentation/Plans/WaterDrivenInfrastructure/MASTER_PLAN.md` when picked up.
- **Pruned:** `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md` (full spec, card-art brief and restore steps stay readable with `git show 09be321aa:Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md`; the last commit holding the feature's code is `fb10224c1`).
- **Evidence:** `ls Documentation/Ideas/WaterDrivenInfrastructure/` listed only IDEAS.md; its one entry read in full (quote above copied from it). `git grep -n "Ideas/WaterDrivenInfrastructure"` found live pointers in this ROADMAP's Long-term Vision (repointed here), `Documentation/Ideas/IDEAS_OVERVIEW.md` lines 37-39 and `.claude/playthrough-test-status.json` lines 3204-3205 (both outside this pass's write scope, left for their owners), and dated records (As_Built docs, earlier entries of this log, CHANGELOG 1.12.1) left as written.
- **Verification debt:** none (no code changed; the feature is not in the mod).

### 2026-10-18 - MASTER_PLAN (vanilla balance and niche content) built and archived
- **Verdict:** DRAINED. Section 1 (vanilla balance, 29 rows) built in WDI 1.12.10. Section 2: the five niche ideas were settled against vanilla data (`Documentation/Research/WaterDrivenInfrastructure/niche_premises_2026-10-15.md`); the fulling mill (GAP CONFIRMED) shipped in 1.12.11; the owner answered the other four in chat on 2026-10-17 (`Documentation/Reports/Vanilla_Balance/wdi-niche-decisions.html`): Retting Pit and Irrigation Channel built in 1.12.12 (`28efa6498`, `9b1dc683e`, release `bf381dfff`), eel weir and ice house dropped to the plan's section 2.1 Considered table. Pack Prompts 1 (Overshot text, `36ec12c6e`), 2 (premises, `a8f6bdbc4`), 6, 8 and the reshaped 4 and 7 done; 3 and 5 not built by owner decision. The fulling mill kit's Disassemble returned wood-less Planks (fixed `32f01291b`).
- **Disposition:** ARCHIVE to `Documentation/Design/WaterDrivenInfrastructure_Vanilla_Balance_As_Built.md` (the Keep and Considered tables, the niche verdicts and the irrigation band arithmetic are design detail worth keeping); the pack is deleted, its final Awaiting verification list folded into the archive as an appendix.
- **Pruned:** the plan and pack left `Documentation/Plans/WaterDrivenInfrastructure/`; live citations repointed (the three section 2 tracker rows' `source`, the WDI Pester balance comment); dated records (earlier entries of this log, `Documentation/Design/AdvancedCopperTools_Vanilla_Balance_As_Built.md`, `Documentation/Reports/Plan_Review/plan-review.html`) left as written.
- **Evidence:** WDI Release build 0 warnings; `WaterDrivenInfrastructure.Tests.ps1`, `Version-Synchronization` and `GameData-VersionLabel` Pester 124 passed, 0 failed on the 1.12.12 tree; the builders' runs of Sprite-NameResolves, CardArt-ShippedForm, Blueprint-TabGroupNames, Perk-StartingItemStats, InventoryCapacity-Model, Passive-Ct13-BackgroundScoped and Localization-EnglishColumnCjk green; `Check-LocalizationParity` CLEAN 673/673; new contracts (retting pit pairs to River.json, irrigation changes only Hydration within the bands) each watched red on a scratchpad copy. Deployed 1.12.12 (DLL 2026-10-18 05:43, release zip `Water_Driven_Infrastructure_1-12-12.zip`); not uploaded to Nexus (live 1.12.11).
- **Verification debt:** T2.1700 to T2.1712 (section 1); `wdi_niche_fulling_mill_quality`; T2.1798 (Retting Pit); T2.1799 (Irrigation Channel, including whether `tag_RiverShore_LowAcidity` / `tag_Wetlands_HighAcidity` are the tags vanilla's shore soils carry).
