# Roadmap: Advanced Copper Tools
Version at time of writing: 1.18.10 (`CHANGELOG.md` `[Unreleased]` holds two 2026-10-19 fixes)
Date: 2026-10-20
Audit score: 10/10 - PASS (0 CRITICAL, 0 Design Gap, 0 open Warning; consolidated 2026-10-20)

> **Rewritten 2026-10-20 after the consolidation.** Score 9 -> 10: the 2026-10-18 file deducted a point no
> `/audit-mod` rubric line supports (`.audit/summary.md` "Score derivation"). Both Phase 1 rows of 2026-10-18
> are done (`cbde015d3`, 2026-10-19), and W3 closed when harness row T2.1547 passed both branches
> (2026-10-18, `8cab38b30`). The Phase 2 and Phase 3 content rows are removed: each is a Considered, not-selected
> row in an archived plan (`.audit/ideas.md` "Already Planned" names where and why). ACT has no open feature
> scope and no `Documentation/Plans/` folder; what remains is in-game verification.

## Current State

**Theme**: Mid-game copper-and-bronze metalworking - a comfort/throughput/light tier layered on vanilla
metallurgy, fully multi-metal (copper -> ghost/tin/tin-bronze/white bronze -> iron) with SD4 metal-type gating
end to end, fed by vanilla ore sources plus Stream Tin panning.

**Content**: 76 items / 66 blueprints / 16 structures (CT2) / 7 perks / 132 custom images (135 sprite
references: 132 mod PNGs, 3 vanilla sprites `Bowl_Clay`, `CastingMold`, `Fat`; 0 unresolved). No WorldMap,
no SelfTriggeredAction, no Trigger JSON (the cave and WorldMap cards left in 1.18.9).

**Stability**: 10/10 PASS - 0 CRITICAL, 0 Design Gap, 0 open Warning, 2 open Minor (M3 two blueprints at the
12-DTP cap, by design; M4 four Debug-level catches in `TeaStationPatch.cs`, a note). Code-quality 10/10.

**Open work**: no open ACT retrospectives. The framework-wide `wikimod-old-save-load-crash` row is Pending
Verification and is not ACT code. Two 2026-10-19 fixes are unreleased: the Wearable Metal Pan's Melt Fat
action could never show (`3de186b1a`) and lit lanterns, oilwell and stove now show their own metal
(`385c8eb16`, T2.1315).

**Framework compliance**: Tier 2. `Api.ActionRouter` in `PanMeltFatPatch`, `SawEffectPatch`,
`TeaStationPatch` and `TinOreSmeltPatch`; patchers registered one by one through `TryApply` in
`Plugin.cs:36-44` (no `PatchAll()`), with `HeatHeldLiquidPatch` config-gated and `HammerGroupPatch` /
`VanillaFireKettlePatch` invoked from `GameLoadPatch`. No deprecated patterns. Versions synced 1.18.10 x3.
Chinese localization present (`SimpCn.csv`, parity clean 1038/1038 at the 2026-10-18 audit).

---

## Phase 0: Stabilize  *(skip - audit score 10/10, no open retrospectives)*

Nothing open.

---

## Phase 1: Foundation

> Table-stakes rows are all met; one release step is owed.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Release the `[Unreleased]` fixes (Melt Fat on the Wearable Metal Pan, per-metal lit art). Nexus serves ACT 1.18.10 (published 2026-10-18T03:39Z, `.claude/nexus-publish-status.json`), so the next version is 1.18.11: bump with `Development_Tools/Update-ModVersion.ps1`, then `/export-to-repo AdvancedCopperTools` and `/nexus-publish`. Both fixes are JSON, CSV and GIF data only. | Release hygiene | P1 | Quick |

---

## Phase 2: Core Expansion

> No row. Every candidate on file is ruled out with a reason: the Copper Ore Crusher (owner gave the
> stamp mill to WDI, 2026-10-01) and the Metal Alloying step (vanilla ships it), both in
> `Documentation/Design/ACT_Content_Update_As_Built.md`'s Considered table. New scope starts as a plan under
> `Documentation/Plans/AdvancedCopperTools/`.

---

## Phase 3: Integration & Depth

> No row. Proximity warmth shipped as far as a vanilla pattern exists (`ACT_Content_Update_As_Built.md`
> feature 1, 1.17.0); the rest is a Considered row. The cave-lantern utility was retired on 2026-10-14 with
> the cave system. Cross-mod hooks with H&F, Sirus23, WDI, CMC and the framework are all wired
> (`.audit/ideas.md` Cross-Mod Opportunities).

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Verification-debt burn-down | Five ACT rows are `pending` in `.claude/playthrough-test-status.json` (read 2026-10-20): T2.1386 (Ore Bin ore-only filter) and the person-only art/feel rows T2.1309, T2.1310, T2.1314, T2.1315. Human-gated, not code work; they live in the tracker. | Medium (human) |
| Refresh the stale sub-audits | items, blueprints, structures, images and perks reports (2026-06-22 to 2026-08-14) and critical-analysis (2026-10-18) predate the 2026-10-15 to 2026-10-19 content commits; a re-run puts their per-card rows on current content (no verdict change expected). | Quick |

---

## Long-term Vision

At v1.18+, ACT is the complete mid-game metallurgy layer: graded sheets, a full copper/bronze/iron gear tree,
heat sources that behave as vanilla fires, and parts shared with WDI and the framework. Its content scope is
closed; its remaining value comes from verification, release cadence and game-update upkeep.

**Potential major additions** (not yet justified): none that is not already ruled out. The Copper Forge and
the Copper Still are Considered rows in `Documentation/Design/ACT_Content_Update_As_Built.md` (vanilla and
sibling-mod forges exist; vanilla `AlembicOn` already distils). Anything worth reviving belongs in a plan
under `Documentation/Plans/AdvancedCopperTools/`, never a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod AdvancedCopperTools` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp.dll`, check CLAUDE.md EA notes, `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis AdvancedCopperTools` to verify the fix |
| Before a release | Run `/export-to-repo AdvancedCopperTools` and bump the version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod AdvancedCopperTools         - full health check, updates .audit/
/critical-analysis AdvancedCopperTools - adversarial review
/repair-items AdvancedCopperTools      - auto-fix item JSON issues
/repair-blueprints AdvancedCopperTools - auto-fix blueprint JSON issues
/build-mod AdvancedCopperTools         - build Release DLL
/deploy-mods AdvancedCopperTools       - build + deploy to game
/update-mod-version AdvancedCopperTools <ver> - bump version in all 3 files
/export-to-repo AdvancedCopperTools    - push to public repo
```

---

## Plan Reconciliation Log

### 2026-09-07 - Audit_Remediation_Plan
- **Verdict:** Fully drained. F1 (MetalSheet SpecialDurability4 rewritten to the real DurabilityStat
  shape, Active with ValidValues [100..140], SD2/SD3 inactive), F2 (Bp_BronzeSheet.json supplies a
  bronze-grade sheet gated at Special4 {110,140}; Bp_MetalSheet gates {0,100}; all four Bronze armor
  blueprints gate the sheet at {110,140} while Copper armor carries no gate), and F3 (README art-reuse
  disclosure) all VERIFIED on disk. Shipped in v1.16.5. 0 open prompts; 0 promotable ideas.
- **Disposition:** ARCHIVE - moved to Documentation/Design/ACT_Audit_Remediation_As_Built.md (the plan
  holds the F1+F2 joint-fix sequencing rationale and the metal-type gate tracing, worth keeping as an
  as-built record). Sibling pack deleted. Executed and committed 9456ee523 (2026-09-07).
- **Pruned:** the entire plan doc (143 lines) and its Implementation Prompts pack (Prompts 1-3) left
  Documentation/Plans/; the AdvancedCopperTools scope folder is now gone. Nothing partially pruned -
  the plan was fully drained, not trimmed.
- **Evidence:** bogus-DurabilityStat-key grep `grep -rlE '"(DurabilityName|MaxDurability|StartDurability|Restricted|IsHidden)"'`
  across AdvancedCopperTools/**/*.json returned 0 hits; control = DurabilityStat-FieldNames.Tests.ps1
  red self-demo fires on the bogus shape (suite 6/6 green incl. red demos), proving the detector can
  hit. Special4-gate parse of every armor+sheet blueprint returned Bp_MetalSheet {0,100},
  Bp_BronzeSheet {110,140}, four Bronze armor {110,140}, six Copper armor NO gate; control = the same
  parse legitimately discriminates Copper (no gate) from Bronze (gated), and an earlier parse using the
  wrong field path returned "no gate" everywhere until corrected. Remainders confirmed homed:
  ROADMAP Phase 4 (optional 5-PNG Bronze/Ore-Chest art) and .audit/ideas.md row 3 (Copper Shield, now
  unblocked since v1.16.5 removed the dead armor-scaling scaffolding).
- **Verification debt:** T2.173 (tier A, the decisive bronze-sheet metal-type transfer, T2.91 depends
  on it), T2.91 (Bronze armor set), T2.155 (armor repair all tiers) - all present in
  .claude/playthrough-test-status.json, all `pending`.
- **Cross-mod tail paid same commit:** WDI 1.10.19 -> 1.10.20. WDI's Cast Metal Sheet had SD4
  activated at copper grade (100.0) on 2026-09-06 because ACT's PatchSheetInterchangeability registers
  it as an alternate wherever ACT's Copper Sheet is required - which as of v1.16.5 includes the Bronze
  gate, and the game skips a Special4 range test when the stat is inactive - but that behavior change
  shipped and deployed under the already-published 1.10.19 label with no changelog entry. Bumped and
  documented (WDI CHANGELOG [1.10.20]).

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

### 2026-09-27 - Content_Update_Plan (ACT 1.17.0)
- **Verdict:** Fully built. Features 1-6 (fire passives for the Copper Stove and Brazier, any lamp oil as fuel, Pan
  for Stream Tin, the Iron Large Saw, the Copper Shield, bronze armor and vein art) shipped in 1.17.0, the Ore Bin art
  in 1.17.1 and the Copper Shield's DMG_Spiritual +50 in 1.17.2. 0 rows unbuilt; the plan had no prompt pack.
- **Disposition:** ARCHIVE - moved to Documentation/Design/ACT_Content_Update_As_Built.md (it holds the Wave 0
  decision branches and each feature's vanilla sourcing). Inbound citations repointed in the same commit: six
  tracker `source` fields, eight `.art` sidecars, Documentation/Ideas/AdvancedCopperTools/IDEAS.md and the WDI
  Content_Update_Plan.
- **Pruned:** the whole plan (one file) left Documentation/Plans/; nothing partially pruned.
- **Evidence:** live harness runs on ACT 1.17.2 (TestHarness 1.3.0, EA 0.68b) on 2026-09-27, each report read step
  by step: T2.320 28/28, T2.321 22/22, T2.322 62/62, T2.323 45/45, T2.324 21/21, T2.325 60/60, no failed step.
  The only allowed-failure steps are an optional popup dismissal (T2.322), an optional popup close (T2.323) and
  T2.321's deliberate Twigs drop, which the game refuses and the next two steps confirm (Fuel still 24, Twigs
  unused). Runtime reads W0-1 (every loaded tag_Oil card: no `NOTCompatibleCards` needed) and W0-2 (the vanilla
  Shield's armor, which led to 1.17.2) are recorded in CHANGELOG [1.17.2].
- **Verification debt:** T2.324 (the fight clause) and T2.325 (the look), both `pending` in
  .claude/playthrough-test-status.json; T2.320 to T2.323 recorded confirmed as r42.

### 2026-09-29 - Alloy_Armor_Plan (ACT 1.18.0, CMC 1.69.3)
- **Verdict:** Fully drained. 12 alloy armor cards and 12 blueprints (Tin, Ghost Bronze, White Bronze), the bronze
  set renamed Tin Bronze and gated at exactly 130, copper armor and the Copper Shield gated at 0-100, SmeltingRecipes
  MetalType on every alloy and iron armor row and the Iron Large Saw, dismantle sheets stamped with the suit's metal,
  12 new ArmorUids, 12 recoloured images, both CSVs; CMC Armorer course locks all 26 armor blueprints and the Copper
  Shield, Metallurgy grants the Bronze Sheet. 0 open prompts.
- **Disposition:** ARCHIVE - moved to Documentation/Design/ACT_Alloy_Armor_As_Built.md (it holds the combat-armor mechanics findings the
  design rests on). The plan was never committed under Documentation/Plans/, so no path there is cited anywhere;
  the four tracker rows and four checklist entries that named it were repointed before commit.
- **Pruned:** the whole plan (one file); the three IDEAS.md rows it promoted are replaced by one SHIPPED note.
- **Evidence:** Release builds of ACT and CMC clean (0 warnings); `ACT-AlloyArmor-MetalConsistency.Tests.ps1` 9/9
  with five planted breaks watched red; the mission gate set (18 files) 218 passed, and the one red
  (PlaythroughRow-Citations, T2.439 cited before it was filed) cleared by filing the rows.
- **Verification debt:** T2.439-T2.443, all `pending` in .claude/playthrough-test-status.json.

### 2026-10-03 - MASTER_PLAN Part I (per-metal art from Chiwei's 0928 set) archived; Part II's built sections collapsed
- **Verdict:** Part I fully built, 0 open steps: Tier A per-metal art and its gate (`551afeab8`), the heated shaped pan (`b68d24baa`), Tier B per-metal art and per-metal stats on the Large Copper Saw, Copper Nail and Small Copper Stove (`1213ec09b`). Part II: 15 of its 18 Selected features built on 2026-10-02 (A1 `b0e8a2244`, A2 `74aee5f27`, A3 `0e90f6680`, A4 `194bfa36a`, A5 `ccfd0ea6e`, B1 `a2a1fb0cb`, B2 `f355414e6`, C2 `62fb5ac05`, C3 `49a66ae66`, D2 `dfe361a9d`, D3 `d98b27002`, E1 transform route `e14b0af57`, E2 `532e710fc`, E3 `bedabb44d`, F3 `c643d8a16`); F1, F2 and F4 stay open, each blocked on another plan (HerbsAndFungi plan features 24 and 23; PartnerOverhaul plan W0.5), re-checked 2026-10-03 07:42.
- **Disposition:** Part I ARCHIVE: moved whole to `Documentation/Design/AdvancedCopperTools_Metal_Variant_Art_As_Built.md` (it holds Chiwei's request, the `AlternateNames` mechanism and the Tier B per-metal-stats decision). Part II stays in `Documentation/Plans/AdvancedCopperTools/MASTER_PLAN.md` and keeps its numeral; its 13 built sections (3 to 7, 9, 10, 12 to 16, 19) are collapsed to one-line pointers with their headings kept, so section-number citations still resolve. Full pre-collapse text: `git show cf796d290:Documentation/Plans/AdvancedCopperTools/MASTER_PLAN.md`.
- **Pruned:** Part I's text from the plan; the bodies of Part II sections 3 to 7, 9, 10, 12 to 16 and 19; Open questions 2 to 8, 11 to 13 and 15 (each built on its default, retired in one line); the risk-register rows of built features. Kept: the Selected and Considered tables, sections 17, 18 and 20 (F1, F2, F4), Open questions 14 and 16, and "## Wave 0 findings" with the owner's C3 and D3 decisions.
- **Evidence:** an `/audit-plan` pass on 2026-10-03 read each built claim's current JSON and C# (a read-only subagent, not commit messages): 15 of 17 claims match their plan text. Two deviate. P1-5, the credit: `ASSET_CREDITS.md` and the README credit line named only the Tier A and iron images, not the 20 other Tier B saw, nail and stove images Chiwei drew; fixed in the same commit. C3: built differently from its section text (a visible CT2 `act_mine_daylight_shaft` in the Metal Mines per the owner's decision, `Patcher/MineDarknessPatch.cs`, and `WorldMap/MapNodes.json` `ConditionalDrops` that reach saves already in progress); recorded on its pointer as a correction. Tracker: all 22 rows the plan names are present (`T2.322` in `confirmedItems`, `T2.750` to `T2.770` pending), so no acceptance criterion lives only in the collapsed text.
- **Verification debt:** `T2.750` to `T2.770` in `.claude/playthrough-test-status.json`, all `pending`; Part I's are `T2.752`, `T2.759`, `T2.760` and `T2.761`. Harness runs still owed: `T2.769`, `T2.770` (Sirus23 loaded) and the `T2.763` re-run.

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 14 Selected features checked against code (A3, A4, A5, B1, B2, C2, C3, D2, D3, E1, E2, E3, F1, F3) and 2 open (F2, F4); 14 built and found in the code, 0 verified done by a person, 0 stale, 0 failed in the plan's own terms (T2.763, C3's portal arrival, is red at the framework and is recorded as such). Pack: 2 open prompts (25, 26), 23 rows awaiting verification, 0 pruned this pass.
- **Disposition:** KEEP IN PLACE (F2 and F4 remain, each blocked on another plan: H&F feature 23, pack Prompt 39; PartnerOverhaul W0.5, its pack Prompt 6).
- **Pruned:** plan section 17 (F1, built 2026-10-04 in `28334ea0a`) collapsed from its full build text to a built-awaiting pointer that keeps its close condition. Pack: no prompt removed; its Awaiting-verification text corrected (T2.769 and T2.770 passed harness runs 2026-10-03, T2.763 still fails at the framework) and Prompt 25 aligned with section 18 (Full and Warm placed tubs only, Open question 14 retired). Plan Status block gained one dated paragraph.
- **Evidence:** a UID probe over every non-bin, non-obj, non-.audit .json, .cs, .csv and .md file under AdvancedCopperTools (245 files): all 20 built UIDs returned 3 to 7 files each (control `advanced_copper_tools_copper_cauldron` 5 files; negative control `zz_no_such_uid_zz` 0). `tag_x_MedicineBrewStation` is in `CopperCauldron.json`, CHANGELOG and README and in H&F's `GameSourceModify/ClayCauldron.json`. `tag_x_Bathtub` returned 0 files in ACT and 0 in H&F JSON and C# (`git grep` hit only an H&F ROADMAP prose line), so F2 is unbuilt; Full and Warm placed tub JSON carry no `tag_x_` entry. `EatFree` and the Eat duty GUID returned 0 files in ACT, and SaltCuredMeat, Oil and SaltCuredFish carry no `CompatibleNPCDuties`, so F4 is unbuilt; PartnerOverhaul's plan still lists W0.5 as the one unanswered Wave 0 item. Tracker script: every T2 id cited in the plan (18) and the pack (18) exists in `.claude/playthrough-test-status.json`, and all 23 rows (T2.750 to T2.771 plus T2.322) are present (T2.750 to T2.771 `pending`, T2.322 in `confirmedItems`). Not run: any Pester gate, build or game.
- **Verification debt:** T2.750 to T2.771 in `.claude/playthrough-test-status.json`, all `pending` (T2.763 red on a framework defect; T2.771 not yet run live); T2.322 sits in `confirmedItems`. F2 and F4 will each need a row when built.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** Part I: already archived 2026-10-03, nothing left in this file but its table row. Part II: 1 Selected feature VERIFIED-DONE (A3: T2.322 and T2.754 confirmed); 15 BUILT awaiting verification (A4, B1, B2, C3, D2, D3, E1, E2, E3, F2 (harness half T2.772 confirmed, person half T2.785), F3, F4, and section 21 steps 1 to 3 with the lanterns, row 1 pricing and the row 4 merge); 2 STALE-CLAIM corrected ("Remaining implementation" item 4 called framework section 30 unbuilt; section 21 step 4 called the WDI and CMC migration and scenario refresh open); 0 FAILED in the tracker (seven 2026-10-07 harness FAILs are unrecorded: T2.755, T2.757, T2.758, T2.760, T2.761, T2.763, T2.764, five of them scenario or fixture faults, T2.757 a suspected defect); 0 OBSOLETE; 3 OPEN rows, each waiting on an owner decision (section 21 row 5 Tin Solder, step 6 iron lantern, the framework half of row 4), plus a non-code hand-off (step 5, rows 11 and 12) and a message (step 7). Pack: 0 open prompts, 21 items on 30 pending rows awaiting verification, 3 OPEN-DECISION lines.
- **Disposition:** KEEP IN PLACE - 3 owner-gated rows remain; the plan now carries a `PLANS-POLICY-EXEMPT:` line naming them, since none can be a prompt until the owner answers.
- **Pruned:** plan Part II section 3 (A3) and its section 0.1 row; section 18 (F2) collapsed to a one-line pointer; the two F1/F2 risk-register rows narrowed to F1; the pack's run-on Awaiting paragraph rewritten as one line per item with its tracker ids (dropping T2.754, confirmed). Full prior text: `git show a11a1fd6f:Documentation/Plans/AdvancedCopperTools/MASTER_PLAN.md` and the matching pack.
- **Evidence:** `git log 72f04b7eb..HEAD -- AdvancedCopperTools/` empty (3 commits touched only the plan, pack and tracker); tracker lookup by id: T2.322, T2.750, T2.752, T2.753, T2.754, T2.759, T2.762, T2.771, T2.772, T2.784 in `confirmedItems`, 43 ACT rows pending, T2.630, T2.993, T2.1232 pending. `git merge-base --is-ancestor` puts `f2ab2ee85` (framework 2.28.0, `StripAllContaminationCheck.cs`) and `dad6da030` (WDI/CMC migration, 9 scenarios refreshed) in HEAD; `git grep act_tin_solder` at HEAD lists 35 ACT blueprints including Bp_CopperStove, Bp_LargeSaw, Bp_LanternOilwell and Bp_CopperTeaKettle and not Bp_MetalLantern (row 5 still open, control: the lantern's removal shows the probe distinguishes); `Bp_MetalLantern.json` sheet Special4 max 140 and `MetalLantern.json` ValidValues 100 to 140 with four `ACT_MetalLantern_*_Iron.png` on disk (step 6 unbuilt); `git grep` of `Documentation/Plans/` for `act_iron_sheet`, `MetalNail` and `sheet-nail-merge` outside ACT's folder returned 0 (no framework-consolidation plan exists; control: the same probe hits ACT's own plan). Reports read under `TestHarness/client/reports/` for every pending ACT row.
- **Verification debt:** T2.751, T2.755 to T2.758, T2.760, T2.761, T2.763 to T2.770, T2.773 to T2.783, T2.785 to T2.788 (ACT); T2.630 (WDI) and T2.993 (CMC) for the merge follow-up; T2.1232 (framework section 30).

### 2026-10-09 - MASTER_PLAN
- **Verdict:** Part II, 17 claims checked against code and tracker: 1 more Selected feature VERIFIED-DONE (D3, T2.766 confirmed), plus section 21 steps 2 and 3 with rows 2, 3 and 9 (T2.779, T2.780 confirmed); 14 BUILT awaiting verification (A1 person-split remainder T2.788, A2, A4, B1, B2, Part I tier B person rows and T2.787, C3, D2, E1, E2, E3, F2, F3, F4, and section 21 step 1, the lanterns, row 1 and row 4); 2 STALE-CLAIM corrected (C3's pointer cited an unrecorded 2026-10-05 pass, now two FAILs; "Remaining implementation" item 1 described the 2026-10-07 runs); 0 FAILED in the tracker (14 harness FAILs of 2026-10-09 are unrecorded, needing triage); 0 OBSOLETE; 3 OPEN rows, each waiting on the owner (section 21 row 5 Tin Solder, step 6 iron lantern, the framework half of row 4). Pack: 0 open prompts, 18 items on 27 pending ACT rows awaiting verification (21 harness lane, 6 person lane), 3 OPEN-DECISION lines.
- **Disposition:** KEEP IN PLACE - not drained: the 3 owner-gated rows stay under the plan's `PLANS-POLICY-EXEMPT:` line (no owner answer since 2026-10-07), and 27 rows still await verification.
- **Pruned:** plan Part II section 13 (D3) and its section 0.1 row; section 21 steps 2 and 3 and rows 2, 3 and 9 cut to one-line done markers (kept so the numbered lists still cite correctly); T2.757 dropped from B1's pointer; pack Awaiting list lost D3, G1b, G1c and the confirmed T2.757, T2.760, T2.761, T2.775, T2.777, T2.782 entries and gained the 2026-10-09 run state and the six person rows. The confirmations behind every cut are `51641ef64`. Full prior text: `git show e5b8406d2:Documentation/Plans/AdvancedCopperTools/MASTER_PLAN.md` and the matching pack.
- **Evidence:** `git log a11a1fd6f..HEAD -- AdvancedCopperTools/` returned 2 commits (`e57d44cfc` README, PLAYER_GUIDE and 6 bin/Release JSON; `9167ca547` this file). Tracker lookup by id: 40 ACT rows pending (control: the same script found the 19 confirmed ids this plan names in `confirmedItems`); every pending ACT row maps in `TestHarness/client/machine_rows.json` except the person rows T2.1308 to T2.1315. The six-rig audit JSON classed T2.774, T2.776 and T2.778 PARTIAL and 14 plan rows FAIL, each failing step read from its report; T2.756 step 19 and T2.781 step 145 read Hidden where Locked was expected. Code probe at HEAD: 16 built UIDs and tags each found in 1 to 4 JSON files (`act_tool_belt`, `act_iron_shield`, `act_mine_daylight_shaft`, `tag_x_Bathtub` among them), `Patcher/MineDarknessPatch.cs` and `Patcher/CopperShearsGatePatch.cs` present, `CompatibleNPCDuties` once each in SaltCuredMeat, SaltCuredFish and Oil; `act_tin_solder` in Bp_CopperStove, Bp_LargeSaw, Bp_LanternOilwell and Bp_CopperTeaKettle and not Bp_MetalLantern (control: the lantern lost its solder in `252226f62`, so the probe separates the two), and `Bp_MetalLantern.json` Special4 max 140 with `MetalLantern.json` ValidValues 100 to 140 (step 6 unbuilt).
- **Verification debt:** T2.751, T2.755, T2.756, T2.758, T2.763, T2.764, T2.765, T2.767, T2.768, T2.769, T2.770, T2.773, T2.774, T2.776, T2.778, T2.781, T2.783, T2.785 to T2.788, T2.1310 to T2.1315 (ACT); T2.993 (CMC, merge follow-up); T2.1232 (framework section 30).

### 2026-10-09 (later) - MASTER_PLAN, second /cleanup-plans pass
- **Verdict:** NOT drained. The pack header's "0 open, PLANS-POLICY-EXEMPT" was stale: 3 open prompts (30 F4 tag_HumanFood on the salt-cured foods, 31 Copper Bathtub GpTag_WaterAny, 32 pan-head cooling), 17 items awaiting verification on 25 rows, 0 owner decisions open. The owner decisions in fb10224c1 opened no ACT code (row 4 built as framework feature 31; row 5 and step 6 declined).
- **Disposition:** KEEP IN PLACE - three owner-approved data fixes remain.
- **Pruned:** section 21 row 5 and step 6 bodies (declined, reasons in section 0.3); row 4, 10 and 12 bodies collapsed to built markers; open question 16 retired; PLANS-POLICY-EXEMPT marker removed; 7 stale claims corrected (CMC hard dependency, CopperShearsGatePatch renamed PartnerBlueprintGatePatch, T2.775 un-recorded, framework move "none exists yet").
- **Evidence:** audited against 1d9cbf121. ACT CardData/Item holds only the retired IronSheet/IronNail (the parts live in CSFFModFramework/CardData/MetalParts/, 5adabf1ab); Community_Mod_Chest/Plugin.cs declares only the framework (a3f965d43); the three MOD_BUG triages match current JSON (SaltCuredMeat/Fish lack tag_HumanFood; CopperBathtub_Empty(_Placed).json RequiredGroupWarpData "tag_WaterAny"; Shape_MetalPan.json Temperature rate 0 with no cooling passive). Gates Plans-Policy 12/12, PlaythroughRow-Citations 13/13, Plans-AwaitingRowIds 5/5.
- **Verification debt:** T2.783, T2.785, T2.787 (fix then rerun); T2.751, T2.755, T2.756, T2.758, T2.763-T2.765, T2.767-T2.770, T2.773-T2.776, T2.778, T2.781, T2.786, T2.788; person rows T2.1310-T2.1315.

### 2026-10-09 (third) - MASTER_PLAN drained: Part II archived
- **Verdict:** 0 open prompts after Prompts 30 to 32 were built in `30460fbe5` (T2.783 `tag_HumanFood` on Salt-Cured Meat and Fish; T2.785 Fill with Water names `GpTag_WaterAny`; T2.787 pan head cooling passives). Every Part II section (A4, B1, B2, C3, D2, E1, E2, E3, F2, F3, F4, G1) reads BUILT; open questions 1 to 16 all retired. Left outside code: section 21 step 5 (row 11, the Ceramic Lamp, a CMC balance question) and step 7 (the reply to Chiwei, the owner's message).
- **Disposition:** ARCHIVE - `Documentation/Plans/AdvancedCopperTools/MASTER_PLAN.md` moved to `Documentation/Design/AdvancedCopperTools_Content_Update_As_Built.md` (it holds the Considered table with its rejection reasons, the Wave 0 findings, the risk register and the step 7 Chiwei draft); the pack `MASTER_PLAN_Implementation_Prompts.md` deleted. Row 11 handed to `Documentation/Plans/Community_Mod_Chest/MASTER_PLAN.md` as J22 with open question 28 (default: leave unchanged until the owner decides). Not moved to `Stable/`: the owner chose the archive only.
- **Pruned:** the pack's Prompts 30 to 32 (their text resolves at `30460fbe5`'s parent) and the pack itself; nothing removed from the plan text beyond status lines marking 30 to 32 built.
- **Evidence:** all 59 commit hashes the plan cites resolve (`git cat-file -e`, 0 missing); all 61 tracker ids cited by the plan and pack exist (39 pending in `items`, 22 in `confirmedItems`); ACT commits since the morning `/audit-plan` at `1d9cbf121` are `3ac78237c` (art), `e76438a1a` (the peer's Ore Bin 1.18.1, tracker T2.1386, not plan work), `b5615f9a4` (CHANGELOG) and `30460fbe5`; Grep of the CMC plan for "Ceramic" returned 0 before J22 was filed, and `Community_Mod_Chest/CardData/Item/CeramicLampLit.json` reads Fuel 48/48 at -0.5. Build 0 warnings; ACT gates 63/63, CardData-Integrity 7/7, localization and csproj gates green, Harness-ScenarioDryRun 6/6. Note: `e76438a1a` bumped ACT to 1.18.1 while the plan's release rule said 1.18.0 (unpublished) takes new work with no bump.
- **Verification debt:** T2.783, T2.785, T2.787 (rerun after deploy); T2.751, T2.755, T2.756, T2.758, T2.763, T2.764, T2.765, T2.767, T2.768, T2.769, T2.770, T2.773, T2.774, T2.775, T2.776, T2.778, T2.781, T2.786, T2.788; person rows T2.1310 to T2.1315.

### 2026-10-11 - Fleet MASTER_PLAN (cross-mod decoupling), /cleanup-plans Fleet apply
- Scope: Fleet (cross-mod plan; the same entry is recorded in the ROADMAP of every mod it names: Community_Mod_Chest, AdvancedCopperTools, WaterDrivenInfrastructure, HomesteadPerks, HerbsAndFungi, Sirus23_Mod_Collection, CSFFModFramework).
- **Verdict:** DRAINED. Features 1-13 all built (1-3, 6, 7, 9, 12 in the commits `33ab18fe9`, `a3f965d43`, `76aac3d8a`, `cc0115b9e`, `fed7f7d0a`, `c6b2bad35`, `31034e212`, `e7b558ead`, `7219cd11d`), 0 open pack prompts, 0 unbuilt rows; every acceptance row recorded CONFIRMED (T2.789, T2.632, T2.997, T2.999, T2.998, T2.549, T2.557, T2.560, T2.1236 to T2.1242). Open question 4 settled by measurement, not by an owner choice.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Fleet_Cross_Mod_Decoupling_As_Built.md` with its Considered table and its drained prompt pack appended; the pack was deleted.
- **Pruned:** plan sections 1, 2, 3, 6, 7, 9 and 12 (each BUILT and confirmed), the stale "awaiting verification" pointers, open question 4's default text (replaced by its measured outcome); pack Prompts 9 and 10 (Prompt 10 had already been built in `e7b558ead`, the pack was stale on that point).
- **Evidence:** on dev lane rig 6 at HEAD (H&F, Harsh Wilds and Sirus23 deployed from the working tree, the rest synced from the real install), harness PASS for T2.557, T2.560, T2.632, T2.997 (both variants), T2.999 and T2.1236 to T2.1242; two independent skeptic reviews of all twelve rows (`.claude/handoffs/skeptic-review-2026-10-11-fleet/r-a.md`, `r-b.md`): 11 RECORD-PASS, and T2.560 FIX-SCENARIO (its brine leg dragged the non-draggable Liquid sub-card, so it passed on any build; fixed in the scenario and rerun PASS with a real refusal, "Dropping 'Pickle Brine' onto 'Wolf Companion' does nothing", beside the same container type with water running Give Water once). Three of the seven absent-partner pins had drifted since 2026-10-09 and were re-measured from real alone-boots (CMC 48 entries, five sprites now resolve; H&F plus twelve of its own map-node forward references; Harsh Wilds plus the Forest Lore `fl_mud_volcano` weight row; WDI now 0 lines), each entry checked against `Documentation/Interop_Registry.md`. End state: `grep BepInDependency(` over every `Plugin.cs` finds 13 files and none names anything but CSFFModFramework (the 13 matches are the control that the scan can hit). Open question 4 probe: with Sirus23 set aside the Wool-Quilted Cap and Vest each keep four requirement slots and the Wool one is null (measured), and a researched blueprint is left alone by `AcademyCourseService.HideWoolQuiltedIfNoWool` (read from code).
- **Verification debt:** none open. One unplayed fact, deliberately without a row: what a craft of an already-researched Wool-Quilted recipe asks for after Sirus23 is removed (the game reads a null slot as no requirement, the fleet audit's reading of the code); CMC's README states it as unplayed. Releases still owed per mod under its own version rules (`.claude/nexus-publish-status.json` against `ModInfo.json`, read 2026-10-11): framework 2.28.0 (Nexus holds 2.26.12), CMC 1.69.6 (1.68.45), ACT 1.18.1 (1.16.10), WDI 1.12.2 (1.11.3), HSP 1.3.1 (1.2.4), H&F 1.14.1 (1.13.7) and Sirus23 1.22.0 (1.21.6); MUM not repacked for this change.

### 2026-10-17 - MASTER_PLAN (iron lantern, vanilla balance pass, niche content)
- **Verdict:** DRAINED. Section 2 (ACT-B1 to B32) shipped in 1.18.9 (`45bf81762`); section 1 (iron Metal Lantern) and section 3 (Salt-Cured Proteins, Tinned Storage Pot, rust and Oil Coat) in 1.18.10 (`bb15e21e1`, released `8dfa697ff`); the Padded Gambeson and salted hides/butter rows were dropped by the owner or ruled out (plan 3.1). The last open row, the ACT-B7 wear-context bench (pack Prompt 1), ran on 2026-10-17.
- **Disposition:** ARCHIVE - moved to `Documentation/Design/AdvancedCopperTools_Vanilla_Balance_As_Built.md` with its Considered tables (2.7, 3.1). The pack `MASTER_PLAN_Implementation_Prompts.md` was deleted (last text at `e2db1cb05`). The 13 tracker rows and the live research, bench and test pointers now cite the archive. The mod stays at the repo root (`Stable/README.md`: no move until fully human tested).
- **Pruned:** pack Prompt 1 and its Awaiting list (its rows are in the tracker); this ROADMAP's "Iron lantern art" row (built in 1.18.10).
- **Evidence:** bench `Documentation/Benches/ACT_Armor_Wolf_2026-10-15/`, rig 3 on Jareds-Desktop, ACT 1.18.10 (rig DLL md5 equal to the Release build), idle machine. Run `20261017-231044` (`--context`): every Usage loss was 250 x `GM.CancelledAtTick` (Rest 1: 250 on 3 hits; Relax 4: 1000 on 3 hits), the result the flat-250 reading cannot produce. Run `20261017-231524` (`--limbs-only`): Copper Bracers +15, equal to Quality 0 Leather Bracers, 250 per blow at `CancelledAtTick` 1. Verdict in `Documentation/Research/AdvancedCopperTools/armor_values_rejudge_2026-10-15.md` section 4: no data change (the scaling is engine code, so leather parity holds). Found beside it, read live: no vanilla animal encounter attacks with `DMG_Spiritual` (Badger, Beaver, Fox Cutting + Piercing; Bear, Boar and the three wolf encounters Blunt + Piercing), so README and PLAYER_GUIDE no longer sell Ghost Bronze as an animal-fight ward, and the "about 185 hits" line now carries the context scaling. **Not checked:** a fight that breaks into Sleep (what `CancelledAtTick` reads there), and anything by a person.
- **Verification debt:** T2.793 to T2.796 (1.18.10) and T2.1765 to T2.1773 (1.18.9), all `pending`; T2.1765's expectation rewritten to the scaled loss while still pending.
