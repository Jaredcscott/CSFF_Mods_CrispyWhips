# Roadmap: Community Mod Chest
Version at time of writing: 1.69.22
Date: 2026-10-23
Audit score: 10/10 - PASS (0 CRITICAL)

## Current State

**Theme**: A broad community-suggested content collection for Card Survival: Fantasy Forest - apparel/armor, character traits, pottery and sculpting, camp decor, fishing gear, and a full east-of-the-river village simulation (Inn with an achievement board, Academy with seven courses, residents with errands and schedules, guards, clay digging, a market stall) plus disciplined optional cross-mod interop. Audited module-by-module (`Modules.json`: 15 modules + a mod-wide bucket).

**Content**: 117 items / 108 blueprints / 97 locations / 9 CT4 environments / 7 CT10 improvements / 60 perks / 256 custom images / 363 GameStats / 95 C# patcher files.

**Stability**: 10/10 - 0 CRITICAL (3 X1 cross-mod refs are HIDE/STRIP-covered), 0 open Design Gap, 2 counted low WARNING (both in the academy module: G5 partial silent-fail-closed on the Professor NPCStat path, G2 stale LogDebug diagnostics; 2 < the 5-warning dock, so the mod stays 10/10 while the academy module itself is 9/10). K33 x4 and A10 diagnostics are by design; 1 open MINOR (S-M2, documentation scope). 13 of 15 modules SOLID with zero open findings.

**Open work**: No red retrospectives. 3 CMC-scoped yellow Pending Verification rows, each needing a live in-game confirmation rather than code: `cmc-guard-combat-no-damage` (law), `cmc-village-hearth-fuel-not-refilling` (civic/academy), `guard-kill-despawn` (law). Plus the two academy code-quality warnings (Phase 1).

**Framework compliance**: Tier 2. ~72 C# files use `ActionRouter` / `SpawnService` / `TickEvents` / `ContentModPlugin`. `ContentModPlugin` base with `SoftDependency` and per-patch `TryApply` (no `PatchAll`), no ModCore/ModLoader/Pikachu dependency, no deprecated `DropCollectionGuardPatch`, no unfiltered hot-path prefixes. Already on the proven H&F architecture.

---

## Phase 0: Stabilize  *(skipped - audit score 10/10, no red retrospectives)*

No blocking work. The 3 yellow retros are human-verification debt, not code defects; they live in the playthrough tracker, not a code phase.

---

## Phase 1: Foundation

> Table-stakes maintenance. The two academy warnings are the only live code items; the rest are satisfied and listed for the maintenance record.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **Academy G5**: add a `WarnOnce(uid, ...)` breadcrumb at `ProfessorSchedulePatch.ResolveNpcStat` null return (`:419-425`) and the `SetNpcStatValue` dict-miss early returns (`:1286-1291`). `1845281bc` closed the `FireAgentAction` + method-resolution halves; this is the half the code-quality report named that it did not reach (module history T2.993 is this exact silent-write class) | Reliability | P1 | Quick |
| **Academy G2**: resolve the three "demote once confirmed" diagnostics still at `LogDebug` (`AcademyPatch.cs:607`, `ProfessorSchedulePatch.cs:894`, `:942`) - delete if the investigation is closed, or raise to `LogInfo` until confirmed; keep in step with any retro naming the line | Maintainability | P1 | Quick |
| Versions synced across ModInfo/Plugin.cs/README (1.69.22 x3) | Version hygiene | - | Done |
| Chinese localization parity (CLEAN 3442/3442) | Localization | - | Done |
| Framework Tier 2 adoption (~72 C# files) | Framework | - | Done |
| Unquoted-internal-comma detection | Localization hardening | - | Done: `Check-LocalizationParity.ps1` row E24 |

---

## Phase 2: Core Expansion

> CMC is already very broad; there is no obvious core-loop gap. Expansion should be demand-driven (new community suggestions), not invented here.

### Refresh the remaining domain sub-audits
**What**: `items-report` and `perks-report` were fully re-run 2026-10-20 (both 0 CRITICAL / 0 WARNING / 0 Design Gaps). The blueprints, structures, environments and images reports still date 2026-09-05 to 2026-09-12 and the feature map to v1.68.16; all predate the latest source commit (`1845281bc`, 2026-10-23). Re-run `/audit-blueprints`, `/audit-structures`, `/audit-environments`, `/audit-images` and `/feature-map Community_Mod_Chest`, then `/consolidate-audit Community_Mod_Chest`.
**Why**: every finding they carry was re-checked by hand and none is open but S-M2, so the reports over-report; `/audit-structures` also replaces S-M2's out-of-date tag table.
**Requires**: none.
**Complexity**: Medium.

---

## Phase 3: Integration & Depth

> Cross-mod interop is already wired and HIDE-gated; depth here is maintenance, not new build.

### Keep the four interop partners healthy
**What**: HerbsAndFungi (hemp, Inn foods), Sirus23 (wool apparel, Inn cheese), AdvancedCopperTools (Inn teas), DurosCoinage (Market Stall coins). Each foreign UID is HIDE/STRIP-gated per `Interop_Registry.md` (`Patcher/AcademyCourseService.cs`) and re-arms if the partner installs later.
**Why**: a partner-mod data rename silently breaks the gated content; the X1 preflight pins (`Preflight-CrossModRefs.Tests.ps1:51-53`) catch structural drift.
**Requires**: coordination with each partner mod's release.
**Complexity**: Medium (only when a partner changes).

---

## Phase 4: Polish

> Text and the human-verification backlog.

| Item | What | Complexity |
|------|------|------------|
| Verify the 3 yellow retros in-game | Guard-combat damage (T4.35), village-hearth refuel (Academy/Village Hall visit), guard-kill despawn; results recorded via `/playthrough-test-plan record` | Medium (human) |
| S-M2 tag-vocabulary scope line | Next `/audit-structures` pass writes the measured non-vanilla tag counts (2026-10-20: `tag_Structure` 65, `tag_Furniture` 17, `tag_Wood` 2, `tag_x_WaterSource` 2 across CT2/CT10). Documentation only; the tags stay | Quick |
| ~~Village Hall README cost~~ | Resolved at HEAD (`README.md:124` now states the full 7-stage total) | Done |
| ~~Perk description CSV/JSON alignment~~ | Resolved (P-F1, 1.68.35 onward) | Done |
| ~~Item authoring consistency~~ | I2/I4/I6 adjudicated 2026-10-20 (inert or resolved) | Done |

---

## Long-term Vision

> Where CMC should be at v1.70+.

CMC is a stable, mature content-and-simulation anchor for the fleet - in active-workspace maintenance, not feature expansion. Its natural endpoint is a village simulation whose every advertised loop is confirmed in-game, with the retro backlog closed, the two academy warnings cleared, and the domain sub-audits refreshed. The biggest justified future work is retiring the diagnostic logging (`GuardCombatDiagnosticPatch`, `CompanionFollowDiagnostics`) once their open retros confirm.

**Potential major additions** (not yet justified - revisit after the retro backlog closes):
- A declarative `Modifiers.json` / `FlavourMatrix` pass if community suggestions call for food-synergy depth - fits the Inn/cooking theme.
- Further Academy courses only if a concrete community request arrives - the seven-course frame is complete as shipped.

Anything worth building belongs in a `Documentation/Plans/Community_Mod_Chest/MASTER_PLAN.md` (the plan library is currently drained); never a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content | Run `/audit-mod Community_Mod_Chest`, the domain audits it names, then `/consolidate-audit Community_Mod_Chest` |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp.dll`, re-run `/diagnose-log` |
| After fixing a defect | Run `/critical-analysis Community_Mod_Chest` to verify |
| Partner-mod release | Re-check the X1 HIDE/STRIP pins in `Preflight-CrossModRefs.Tests.ps1` |
| A yellow retro confirmed in-game | `/resolve-retro <slug>`, then demote its diagnostic logging |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Community_Mod_Chest          - preflight + mod health check, updates .audit/
/consolidate-audit Community_Mod_Chest  - merge .audit/ reports into summary, ideas, ROADMAP
/critical-analysis Community_Mod_Chest  - adversarial review
/repair-items Community_Mod_Chest       - auto-fix item JSON issues
/repair-blueprints Community_Mod_Chest  - auto-fix blueprint JSON issues
/build-mod Community_Mod_Chest          - build Release DLL
/deploy-mods Community_Mod_Chest        - build + deploy to game
/playthrough-test-plan                  - file/record the 3 yellow-retro checks
/export-to-repo Community_Mod_Chest     - push to public repo
```

## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b. Append-only: never reword, reorder or delete an entry.
> `/roadmap` Step 7 preserves this section verbatim when it overwrites the rest of this file.

### 2026-09-07 - Audit_Remediation_Plan
- **Verdict:** 2 fix rows, 0 feature rows. **F2 BUILT this pass** in v1.68.21; **F1 confirmed already
  closed** (re-derived, not inherited); the 4 former feature rows N1-N4 had already been withdrawn to
  the plan's third-state table on 2026-09-07 with their recorded rejection reasons, and 0 rows
  remained buildable after F2 landed. The 1-prompt pack is drained.
- **Disposition:** DELETE from `Documentation/Plans/Community_Mod_Chest/` and ARCHIVE to
  `Documentation/Design/CMC_Audit_Remediation_As_Built.md` (plan verbatim + its prompt pack as an
  appendix), per `Documentation/Plans/README.md` step 4. Nothing was erased: every withdrawn row's
  rejection reason travels with the archived doc, and the four N1-N4 reasons were already stamped
  into `Documentation/Ideas/Community_Mod_Chest/DEFERRED_ITEMS.md` by the 2026-09-07 pass.
- **Pruned:** the whole plan doc and the whole 1-prompt pack, both now archived rather than dropped.
- **Evidence:** F2, as built. `CardData/Location/` enumerated by CardType+UniqueID: exactly **7 CT8
  cards**, matching F2's list one-for-one, and `grep -c EnvironmentImprovements` returned **0** on all
  7; a `grep -rl '"CardType": 8'` across the rest of `CardData/` returned **0**, so the set of 7 is
  complete and the plan's warning about `cmcJailCellLocation` not being named `*InteriorLocation` is
  real. `InjectImprovementInto.json` held **11** entries (2 other improvements + 9
  `cmcimpstonetilefloor`); those 9 reverse-mapped through
  `Documentation/GameData/CSFF-JsonData_Current/UniqueIDScriptableGUID/CardData.json` to the vanilla
  Cabin/MudHut family only (Cabin, its StartingPoint / CabinRoom / Attic construction cards, MudHut,
  its Left/Right expansions and its Room / StartingPoint cards), confirming zero CMC targets. Now
  **18** entries; the diff is append-only (`git diff --numstat` = 8/1, the single deletion being the
  comma added to the previous last line) and the 11 originals were asserted byte-identical after the
  write. `CSFFModFramework/Injection/ImprovementInjector.cs` read in full: `TargetEnvUID` resolves
  through a UniqueID lookup over `allData` + `GameRegistry.AllUniqueObjects`, so a plain mod UID is
  correct and a GUID hash would be wrong here; injection is idempotent and warns on an unresolved
  UID. All 7 targets carry `AlwaysUpdate: false`, so none is in the CT4/CT8 travel-softlock shape.
  Release build clean (0 warnings, 0 errors); `bin/Release/InjectImprovementInto.json` parsed and
  compared equal to source (18/18); deployed copy at
  `BepInEx/plugins/Community_Mod_Chest/InjectImprovementInto.json` parsed at 18 entries.
- **Evidence, F1 (closed; re-derived rather than inherited):** the plan asserted DG-F1 was resolved
  upstream in framework 2.25.28. Confirmed by reading
  `CSFFModFramework/Loading/AlwaysUpdateService.cs`: `EnableAll` now carries a `skippedInert` branch
  and the header comment "For all OTHER mod cards we want AlwaysUpdate = true, EXCEPT inert CT2
  fixtures (DG-F1)", with per-cause warn-once fallbacks to the pre-DG-F1 behaviour on a reflection
  miss. The force-set the row was written against no longer runs for inert CT2 cards, so F2 was the
  only buildable row and F1 needed no CMC-side change.
- **Docs-honesty, both directions:** the fix made two existing claims stale in the UNDER-claiming
  direction, which the F2 row did not anticipate. `Imp_StoneTileFloor.json` `CardDescription` and
  `README.md:44` both said the floor could be laid "in cabins and mud huts"; both now name the seven
  village interiors, and the CSV rows were updated in the same pass (`SimpEn.csv` line 529,
  `SimpCn.csv` line 589, English columns byte-identical, Chinese retranslated).
  `Check-LocalizationParity.ps1` reports CMC CLEAN at 2273/2273. `ModInfo.json` `Description` needed
  no change: it never mentioned the Stone Tile Floor (`grep -c -i tile` = 0).
- **Verification debt:** **T2.187** (`cmc_stone_tile_floor_village_interiors`, tier B, `pending`) in
  `.claude/playthrough-test-status.json`, carrying both halves of the acceptance check: the floor
  actually appearing and building in at least two CMC interiors including `cmcJailCellLocation`, and
  no `ImprovementInjector: ... not found in registry` warning for any of the 7 UIDs at load.
  Plan-lifecycle gate 2 is therefore satisfied. F1's own residual in-game check remains **T2.185**
  (framework-owned, unchanged by this pass).
- **Blockers a future implementer must not step on:** the archived doc's third-state table is the
  record for 14 candidates that must NOT be re-promoted, including the four withdrawn feature rows
  (Sling, Bone Helmet, Grinding Slab Kit, Stone Block). Bone Helmet in particular shipped in 1.16.0
  and was deliberately removed in `e9fabc27d` with **no reason recorded anywhere**; re-adding it
  re-litigates a lost decision and needs that reason established first.

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

### 2026-09-12 - Release_Hardening_Plan (Phases 1-3 of 4)

- **Verdict:** 8 Phase 1 rows, 2 Phase 2 rows, 2 Phase 3 rows, 5 Phase 4 rows. **Phases 1-3 are
  complete and pruned out of the plan doc; Phase 4 remains and is BLOCKED on a human reproduction.**
  Phase 1: 4 rows BUILT in CMC 1.68.24 (H2, H3, H4, H6), 3 rows REFUTED and deliberately not built
  (H1/W3, H5/W18, H7/W8+M1), 1 row closed by owner decision (H8/M2). Phase 2: both rows landed, the
  audit re-scored 8/10 -> 9/10. Phase 3: both rows closed as "precondition never met, diagnostic
  retained", which this plan's own Done gate allows; BOTH diagnostics stay in the tree. Phase 4: 0
  of 5 rows buildable today, so the plan doc stays in `Documentation/Plans/`.
- **Disposition:** KEEP (pruned). Not DELETE and not ARCHIVE: A2-A5 are genuine unbuilt code work,
  so `Documentation/Plans/` still answers "what needs building?" truthfully for this file. Phases
  1-3 were deleted from the doc per `Documentation/Plans/README.md`'s no-finished-work-history rule;
  A1 was collapsed to a one-line pointer at its tracker row, per the same README's section rule.
- **Pruned:** the whole of Phases 1, 2 and 3 plus their ground rules, and the Phase 4 A1
  reproduction procedure (now `T2.236`). Nothing was erased: every refutation's evidence is written
  into `Community_Mod_Chest/.audit/summary.md` against its own finding row and into the CMC 1.68.24
  CHANGELOG entry, and both Phase 3 preconditions are written into their tracker rows in full.
- **Verification debt filed:** `T2.236` (Achievement Board reproduction on a dev build, carrying the
  whole static clearance so nobody re-checks it) and `T2.237` (companion-follow observation window,
  carrying the correction to BOTH mistaken readings of the live log). Tracker went 151 -> 153 rows,
  `git diff --numstat` +18/-0.
- **Evidence:** Phase 1 (built). `CMC_AshBoarTrail.json` `git mv`'d `CardData/Item/` ->
  `CardData/Location/`; it is `CardType: 2`, and `CSFFModFramework/Loading/JsonDataLoader.cs`
  `DirToTypeName` was read in full to confirm it keys on the top-level `CardData` folder only, so
  the subfolder is organisational and the unchanged UniqueID `cmcAshBoarTrail` means saves are
  unaffected. `CMC_TownWoodPile.json` given an explicit `"AlwaysUpdate": false`: its 4 siblings were
  identified by reading the `cmcEnvVillage` node's `ConditionalDrops` list in
  `WorldMap/MapNodes.json` (`cmcInn`, `cmcAcademy`, `cmcJail` and one GUID entry, all `ForceStay:
  true`), and the 3 named ones were confirmed to carry `AlwaysUpdate=False` already;
  `CSFFModFramework/Injection/ConditionalDropService.cs` `RegisterNode` was read to confirm it
  already forced that value, so the change is provably zero-runtime. Two `CardDescription` blocks
  added with one `SimpEn.csv` and one `SimpCn.csv` row each in the same commit;
  `Check-LocalizationParity.ps1` CLEAN at **2280/2280** across the fleet. 20 `Pk_*.json` files given
  their missing keys, after reading `CSFFModFramework/Data/WarpResolver.cs:233` to confirm the
  warpType-3 path skips an already-populated array, and after confirming the only 2 perks with
  populated warp arrays were not missing those keys. Every JSON write ran through a guarded script
  that asserted each anchor matched exactly once, re-parsed the result, compared the parsed object
  against the pre-image field by field, and asserted the line-ending delta; 3 files whose byte
  format could not be reproduced were refused by that guard and rewritten by targeted textual
  insert instead. Build 0 warnings 0 errors; commit `8d5656afa`, 60 files, +141/-43, staged numstat
  compared against the commit's own and matching exactly.
- **Evidence, Phase 1 (refuted, and each would have shipped a regression or pure churn).** W3: read
  `.decomp/CharacterPerk.cs` and found `public int StarsCost` at line 11, plus `IsPurchasable`
  returning `StarsCost > 0` when `SunsCost` and `MoonsCost` are both 0, which is exactly
  `Perk_Claws` (0/0/1). The row asked for the key to be DELETED; that would have made the trait
  unpurchasable. Only the key's alphabetical position was actually wrong. W18: traced the single
  consumer `CSFFModFramework/Api/GameQuery.cs:306-336` to `EnvID.MainEnvCard`, read
  `.decomp/EnvID.cs` to confirm that field, then proved it is the CT4 by finding `CMC_Inn.json`'s
  travel DismantleAction dropping `cmcInnInterior` (CT4), and confirmed all 7 CT4 partners already
  carry `tag_EnvIndoors` while `EncounterGuards/CMC_InteriorsNoWildlife.json` keys on those same 7
  CT4 UIDs by name rather than by tag. A sweep of all 14 mods found **0** cards of any CardType
  carrying an environment tag on a CT8. The vanilla JSON export was checked first and discarded as
  evidence: it serialises no resolved tags at all (0 hits across 2841 files), the same misleading-
  precedent trap CLAUDE.md records for `InventorySlots`. W8/M1: swept all **79** `Bp_*.json` for
  the classes that actually have a runtime consequence and found **0** over the `BuildingDaytimeCost`
  cap, **0** `*WarpData` missing its `*WarpType` sibling, **0** `(0,0)` result quantities, and all
  **38** files lacking a `CardImage` key carrying a resolvable `CardImageWarpData` instead.
- **Evidence, Phase 2 (re-score).** The red-retrospective term went to zero. Verified by PARSING the
  status column of `Documentation/Retrospectives/INDEX.md` rather than grepping the glyph: 0 red, 13
  yellow, 9 graduated. A raw grep returns 1 red, and that single match is a prose mention inside
  `cmc-village-hearth-fuel-not-refilling`'s own row describing a status corrected on 2026-09-05,
  which is exactly the false-positive shape CLAUDE.md's measurement rule warns about. This was
  already predicted in summary.md's own Retrospective Status section on 2026-09-09.
  Score = 10 - 0 (red retro) - 0 (CMC-owned DESIGN GAP) - 1 (5 open WARNINGs, flat threshold) =
  **9/10**. The open-row count was then verified by classifying all 26 W/M rows and asserting the
  classified count equalled the input count, so a silently-unparsed row could not inflate the
  result: 21 closed, 5 open (`W10`, `W12`, `W13`, `W14`, `W15`), 0 open minors. Each of the 5
  survivors carries a written reason it stays open, which is R2's stated alternative acceptance;
  4 of the 5 are human-verification debt and the fifth is structural.
- **Evidence, Phase 3 (both preconditions unmet, both diagnostics retained).** D2: the retro
  `cmc-guard-combat-no-damage` reads yellow Pending Verification in INDEX.md and needs `T4.35`,
  which is a human reading a guard fight's `Approach <EncounterName>` line. Untouched. D1: this row
  named a `Player-prev.log` line as candidate confirming evidence. Read the LIVE
  `BepInEx/LogOutput.log` (the repo-root copy is a stale 539,086-byte partial against the live
  565,839 bytes, per CLAUDE.md's deploy-verify rule) and found BOTH obvious readings wrong. The
  "(caught up)" line at 3245 fires while the player env is `2b19b942a09fdd148a43798e942a74eb`, a
  VANILLA env, so it proves nothing about CMC nodes. The later "NOT in the player's env" line at
  3565 looks like proof the bug is live, but the session ended 358 lines later with ZERO further env
  changes, and the diagnostic logs only on a stuck<->caught-up transition (15s poll, dedup key
  "same"/"diff", confirmed by reading `CompanionFollowDiagnostics.Run`), so no second sample was
  ever taken. The genuinely new result is in that dump's move-probe:
  `MoveDestination=MoveToPlayer mapPath.IsValid=True StepCount=2 End=cmcEnvVillagePath`, i.e. A*
  into a CMC node RESOLVES and the `MapDict` root cause this diagnostic was written for IS fixed.
  What blocks `PartnerDuty_Follow` now is "Conditions are not valid", and that duty has exactly ONE
  non-default condition, `InBackground`, confirmed from the vanilla NPCDuty export;
  `.decomp/GeneralCondition.cs:237` force-fails any duty carrying `InBackground`/`NotInBackground`
  while `GameManager.IsTravelling`, which is a plausible benign explanation for a sample taken at
  the moment of arrival. All of this is written into `T2.237` so the next reader does not repeat
  either mistake.
- **Evidence, Phase 4 (not built, and why no fix was attempted).** The whole static surface was
  cleared and came back clean: 22 DAs forming 11 locked/earned pairs, each with `AlwaysShow: true`,
  a unique `ActionName.DefaultText` and a unique `LocalizationKey` (so the
  `InspectionPopup._AlreadyDisplayedActions` same-name dedupe is ruled out); 44 CSV rows present in
  BOTH `SimpEn.csv` and `SimpCn.csv` with 22 distinct English `_Name` values and 0 duplicates; all
  **79** `cmcStatAch*` UniqueIDs referenced by the card and by the three patches resolving to
  shipped `GameStat/` files with 0 typos and 0 orphans; and the hide mechanism correct by default,
  traced through `.decomp/DismantleActionButton.cs` (hides when `StatsAreCorrect` is false AND
  `MissingStats` is empty) and `.decomp/CardAction.cs:1051` `StatsAreCorrect` (leaves `MissingStats`
  empty when neither `HideAllWhenNotMet` nor `NotifyWhenNotMet` is set, which is this card's case).
  `VillageHallBoardsPatch` was found ALREADY enabled in `Plugin.cs` and already owning
  `cmcBoardAchievements` including the summary line and 6 progress counters. Since no static defect
  exists, shipping a fix would be fixing an unconfirmed hypothesis, which CLAUDE.md Debugging
  Discipline rule 2 forbids, so the reproduction was filed as `T2.236` with the clearance attached
  and nothing was changed. Re-confirmed the board is still unadvertised (0 "achievement" mentions in
  `ModInfo.json` and `README.md`), so the bench remains docs-honest.
- **Incidental fix, outside the plan.** `Community_Mod_Chest/Features.json` did not parse: two
  unescaped `"` pairs inside one string value on line 775. Bisected to commit `12ed4991f` (CMC
  1.68.20, 2026-09-09); it is dev-only metadata, absent from the csproj, from `bin/Release` and from
  the deployed folder, so no player was ever affected. Repaired by escaping exactly 4 characters on
  that one line, asserted to change only line 775 and to leave the file parsing.

### 2026-09-13 - Release_Hardening_Plan (Phase 4, plan complete)

- **Verdict:** Phase 4's 5 rows, 0 left buildable, so the plan is Done. A1 (human reproduction) SUPERSEDED rather
  than performed: the diagnosis it was waiting for was reached from shipped code and data, so `T2.236` was
  re-purposed from a reproduction request into the acceptance check. A2 BUILT as the retro
  `cmc-achievement-board-presentation`. A3 BUILT (layout fix, three patches re-enabled, Inn spawn restored). A4
  BUILT (`ModInfo.json`, `README.md` and `CHANGELOG.md` claim the board). A5 BUILT (tracker rows below).
- **Disposition:** ARCHIVE to `Documentation/Design/CMC_Release_Hardening_As_Built.md` (the plan text unchanged,
  below an as-built header) and remove it from `Documentation/Plans/Community_Mod_Chest/`, per
  `Documentation/Plans/README.md` step 4. Version: folded into the unpublished 1.68.24 instead of the plan's
  1.69.0, because `.claude/mod-publish-status.json` records CMC's last publish at `480bb9e50`, which carries
  1.68.23, and MUM's last publish carries 2.1.37, not the 2.1.39 that embeds 1.68.24.
- **Pruned:** the whole plan doc (Phase 4, its not-in-this-plan list and its Done gate), archived rather than
  dropped.
- **Verification debt filed:** `T2.236` (fresh-save board shows the summary and all eleven lines, with the
  objective `truncated=False` log line), `T2.168` (the 11 detectors; re-pointed from an r33 SKIP that ran on the
  benched build, status back to pending), and new `T2.238` (fresh-save presence, old-save backfill and the popup
  restore check, re-arming `T2.170` and `T2.169`, which stay confirmed as history). `T2.237`'s source was
  repointed at the archived doc.
- **Evidence:** Root cause. `VillageHallBoardsPatch.UpdateActionsVisibility` hides `DismantleOptionsParent` for
  every board, so entries exist only as prose in `InspectionPopup.DescriptionText`; `ApplyDescriptionSizing` sets
  that text to `TextOverflowModes.Truncate` with a 60% auto-size floor, and `.decomp/InspectionPopup.cs` has no
  `ScrollRect` on it (its only one is `InventoryScrollView`). Both came in with `5a77611a6` (2026-07-29), before
  the board. Simulating every board's fresh-save output from its JSON and the game's own range test gave the
  achievement board 19 paragraphs / 1047 characters against 4-8 paragraphs for the other seven (largest Weaver,
  7 / 698), with the summary printed last. Of the candidates, only a tail cut removes several entries AND the
  summary. The plan's A1 "hide mechanism" row had traced `DismantleActionButton.Setup`, which never runs for a
  board.
- **Evidence, counter-claim checked.** `T2.170` was recorded PASS in r33 (2026-09-06) as "board present, 11 locked
  lines, 0 of 11 counter" on a build where the board could not spawn in a fresh run. All 85 JSON files under the
  game's save folder were scanned: the board is in 2, both `Game_0`, last written 2026-08-28; the control card
  `cmcBoardInnKeeper` is in 11.
- **Evidence, build and gates.** Fresh-save output is now 3 paragraphs / 493 characters. The source patch asserted
  every anchor exactly once and ran all validations before writing; the Inn drop list equals the pre-bench list at
  `8d31b665d^`, and no other key in that file changed. Before the build only the three patched source files were
  dirty in `Community_Mod_Chest/`. Release build 0 warnings / 0 errors; the DLL carries `AppendAchievementSections`,
  `ProgressSuffix`, `GetVisibleBoardActions` and `LogBoardFit` (UTF-8) and the `text layout: lines=` literal
  (UTF-16-LE), and 0 occurrences of `AppendAchievementStatusLines`. `Framework-PerRunHandlerRegistration.Tests.ps1`
  5/5. All 72 vanilla GUIDs hard-coded in the tracker and kill-effect patches resolve on EA 0.67i; none of the
  board's source, data or helper files changed between the bench and this fix.

### 2026-09-17 - Trait_Effect_Repair_Plan (closeout)
- **Scope:** Fleet. The same entry is in `Community_Mod_Chest/ROADMAP.md`, `HerbsAndFungi/ROADMAP.md`, `Sirus23_Mod_Collection/ROADMAP.md` and `CSFFModFramework/ROADMAP.md`, the four mods the plan names.
- **Verdict:** every phase built: P1 (CMC 1.68.25), P1b (1.68.26, `1f71be4a7`), P1c (1.68.27, `b5d93a885`), P7 (CMC 1.68.28 and HerbsAndFungi 1.13.1, `e4fad82ab`), P2-P6 (CMC 1.68.30, Sirus23 1.21.3, CSFFModFramework 2.26.0, `7b1c68f64`), and the pack's last open prompt, Prompt 1 (delete the temporary `Community_Mod_Chest/Patcher/TraitDiagnostics.cs` tracer), in `8d25ed810` with no version bump. Prompt 1 was gated on playthrough row T2.239, which is still pending; the owner lifted that gate on 2026-09-17 ("We need to be able to proceed with code work without being blocked on a full playthrough"). The plan doc's own row inventory was compared against the pack rather than trusting its 0-open count, and three plan items with no recorded outcome were settled this pass, none needing code: section 3.6's pre-release check (no `TriggerRange` on the four infection stats Deadly Disease rate-modifies), section 3.2's Swimmer 5 Sun re-check (kept), and section 3.1's "drop madness from Lunacy's text" (already done in 1.68.30). 0 plan rows unbuilt.
- **Disposition:** ARCHIVE to `Documentation/Design/Trait_Effect_Repair_As_Built.md`: the plan with an archive banner, dated notes for Prompt 1 and the three checks above, and the drained pack appended as an appendix with Prompt 1 removed. Pack `Documentation/Plans/Fleet/Trait_Effect_Repair_Plan_Implementation_Prompts.md` deleted. The plan holds the only record of the ground truth behind the rework (R1-R20, D1-D9) and of every deviation from its starting values, so it is kept, not deleted. Not published: the public releases are still CMC 1.68.24, HerbsAndFungi 1.13.0, Sirus23 1.21.2 and CSFFModFramework 2.25.32 (`.claude/mod-publish-status.json` commits read back through each `ModInfo.json`), and the export is the owner's call. R14/R16's reading of the player's "+1.2 Speed" as the +1.2 Aid rate is still unconfirmed with the player; it is recorded in the archive's section 1.
- **Pruned:** Prompt 1 from the pack before the pack was folded into the archive and deleted; nothing else. The plan left `Documentation/Plans/Fleet/` whole.
- **Evidence:** Prompt 1: `dotnet build -c Release` 0 warnings 0 errors; the built `bin/Release/Community_Mod_Chest.dll` holds `TraitDiagnostics` (UTF-8) 0 times and `[TRAITDIAG]` (UTF-16-LE) 0 times, against controls `RiverSwimPatch` 1 and `[RiverSwimPatch]` 9; `grep -rn TraitDiagnostics --include=*.cs Community_Mod_Chest` 0 lines (control `RiverSwimPatch` 15); no file under `Documentation/Retrospectives/` cites `TRAITDIAG` or `EnableTraitDiagnostics`. Plan rows against code: `Community_Mod_Chest/GameStat/` holds all nine `CMC_Trait*.json` (eight trait stats plus `CMC_TraitSkinShelter.json`); `TraitsTickHandler.cs`, `TraitsActionHandler.cs` and `TraitDiagnostics.cs` absent from `Patcher/`; gates (a)-(e) plus the extended river swim test present as `TraitStat-CompositeBands`, `Perk-HeldTestNotAllPerks`, `PerkAidRate-HoldsTier`, `PerkStatClamp-Reachability`, `StatBase-NoVanillaSource` and `CMC-RiverSwim` `.Tests.ps1`; `CSFFModFramework/Patching/PerkOriginTagPatch.cs` and `Discovery/ModTag.cs` present, `[Perks] ShowModOriginTag` bound at `CSFFModFramework/Plugin.cs` and documented in its README config table; 12 `ModInfo.json` files carry `ShortName`; 21 Sirus23 JSON files reference `SaturationDairy` (by name or its UID `f4b08d0250e6099419e010a83578b9db`); `ModInfo.json` versions CMC 1.68.30, HerbsAndFungi 1.13.2, Sirus23 1.21.3, CSFFModFramework 2.26.0. Section 3.6 check: a JSON walk of 29,435 files (the vanilla EA 0.67i UniqueID and ScriptableObject exports plus every mod folder, 0 unparseable) found 0 `TriggerRange` objects whose `StatWarpData` is Infection_Gastrointestinal `1a8d37787d69c9b4aa05d332921f3763`, Infection_UpperRespiratory `3407bfc804966194e9e369a7cce6d07d`, Infection_Systemic `dc3cae53109fd5945b5279ec6291caae` or Infection_LowerRespiratory `fa47d156a14dac842bcdcbdbf8504e35` (by UID or name), with the same walk finding the control, vanilla `Tgr_Anxiety` on Stress at 240; `.decomp/` (949 `.cs`) names no infection stat (controls `HourOfTheDayValue` 4 files, `StatValueTrigger` 13), and no mod `.cs` references the four UIDs. Section 3.2: vanilla `CharacterPerk` SunsCost is only ever 0, 15 or 30 (71/38/20 of 129), and Swimmer's 5 matches the six other five-Sun CMC perks (CMC's SunsCost counts: 0 x20, 1 x21, 5 x7, 15 x5, and 10, 20, 25, 30, 100 once each), which include Abundant Growth's +1.2 Aid rate and Wide Hands' +15 skill offset; Swimmer now delivers what its price was set for. Section 3.1: `madness`, `insan` and `mania` absent from `Pk_Lunacy.json`, `CMC_TraitLunacy.json` and its CSV rows. Tracker: T2.239 and T1.84-T1.114 all present in `.claude/playthrough-test-status.json`, all `pending`, so lifecycle gate 2 holds; the 33 plan and pack path citations in it and the one in `Playthrough_Test/Playthrough_Checklist.html` were repointed at the archive in the same commit.
- **Verification debt:** T2.239 (Nyctophobia and the tracer's log lines), T1.84-T1.88 (P1b), T1.89-T1.97 (P1c), T1.98-T1.101 (P7 medicine), T1.102-T1.109 (P2 condition traits), T1.110-T1.111 (P3 swim and Aid), T1.112 (P4 Sirus23 dairy), T1.113-T1.114 (P5 origin tag and clock hour), all `pending`. T2.239 part (5), T1.114 part (4) and the optional evidence in T1.102-T1.108 read `[TRAITDIAG]` lines that CMC 1.68.30 on the dev install still prints until the next CMC deploy and no later build prints; each of those rows carries a dated 2026-09-17 note saying so.

### 2026-09-26 - Guard_Subdual_Summon_Plan
- **Verdict:** not implemented when checked. No guard Encounter but `cmcEncounterSterlingArrest` carried `EncounterResult` 8, exactly as the plan's section 1 table described. Built this pass as CMC 1.68.46: all five build steps done, 0 rows left buildable, no pack existed. Two plan premises were wrong and were corrected while building. (1) Section 3 copied `RequiredWrestlingState` 2 from the Sterling arrest "so it cannot fire at range or on the opening round"; `.decomp/EncounterWrestlingCondition.cs` makes 2 `NeedsNOTWrestling`, which every guard action already carries, so it gates nothing of the kind. The subdual keeps 2 anyway: `InGameEncounter.SelectAction` picks `list[0]` when every weight is 0, so an `AnyState` subdual would be the only surviving, and so the chosen, candidate whenever the player wrestles. It is also appended LAST for the same reason. Gating comes from weight instead: `BaseWeight` 0 plus `StatsWeightModifiers` on the player's Bruising 151-400 (+50..200), Fear 61-100 (+25..100), BloodLoss 5-20 (+25..100) and Stamina 0-15 (+50..0), each band the stat's own vanilla status band, against the spear attack's 100. `RequiredDistance` stays close range like its siblings. (2) Section 3 said Thorne's and Corrin's existing `PlayerDemoralizedEffects` "become live exactly as 1.45.0 describes"; they also raised `cmcStatArrestPending`, and `JailPatch.OnArrestPendingRaised` writes a sentence the moment it rises while `OnDayRollover` counts it down with no location check. Once the subdual made that branch live, a lost fight would have started a sentence served, and Village Crime cleared, while the player walked free. Removed from both; only Sterling's three encounters raise it now. Vane's `PlayerDemoralizedEffects` changed as section 3 asked (jail move and arrest flag out, `cmcStatGuardsSummoned` in), still unreachable by design.
- **Scope extension:** `cmcEncounterGuardSterling` and `cmcEncounterSterlingConverge` had the same defect (the plan's own table marks both "No") and got the same fix, a Sterling-voiced subdual, so their existing arrest `PlayerDemoralizedEffects` (Bruised, `cmcStatArrestPending`, move to `cmcEnvJailCell`) are reachable. The documented intent is arrest: the 1.45.0 CHANGELOG ("the player he catches is left Bruised on the way to the cell") and README ("Either way you end up caught"), and the T4.27/T4.29 walkthrough notes had predicted that no Sterling fight could jail. `cmcEncounterSterlingArrest` stays the one forced arrest (plan section 2). Also fixed in passing: README claimed putting all four guards down resets Village Crime by itself; `CMC_InnKeeperTalk_GuardsConfession` (gated on `cmcStatGuardsAllDown`, -999 crime on either answer) is what clears it.
- **Disposition:** ARCHIVE to `Documentation/Design/CMC_Guard_Subdual_Summon_As_Built.md` (plan text unchanged below an as-built header) and removed from `Documentation/Plans/Community_Mod_Chest/`, per `Documentation/Plans/README.md` step 4.
- **Pruned:** the whole plan doc, archived rather than dropped.
- **Verification debt filed:** new `T4.43` (lose to Thorne or Corrin: grab, summons, no sentence, Sterling's lenient offer; also asks whether the grab came too early or too late, since the weights are a first pass) and `T4.44` (lose to Sterling or a converge: jailed Bruised, one sentence). `T4.27` and `T4.29` re-armed from skip to pending with a dated note. All four are in `Documentation/Playthrough_Test_Plan.md` and the HTML checklist. The TestHarness scenarios for T4.27 and T4.29 still carry pre-1.68.46 prose (their `human` and `fails_when` text predicts that no Sterling fight can jail and that Vane's jail move fails the data step); those files sit uncommitted in another session's TestHarness lane and were left alone, and both tracker notes say the prose is stale.
- **Evidence:** a selected enemy action's result applies whether it hits or misses: `EncounterPopup` calls `ResolveEncounterResultFromEnemyState` after every resolved round, `InGameEncounter.AnyResultFromEnemyState` reads `CurrentEnemyAction.EncounterResult` directly, and PlayerDemoralized is checked last (a kill, rout or escape in the same round wins). `DoesNotAttack` makes `GenerateAndApplyPlayerWound` return before any wound. `StatBasedDropChanceModifier` reads the PLAYER's stat (`StatsDict[Stat].SimpleCurrentValue`), while `ValuesWeightModifiers` and `WoundsWeightModifiers` read the enemy's own values. `CSFFModFramework/Data/WarpResolver.cs` writes struct array elements back after walking them (lines 472-473), so a GUID `StatWarpData` inside `StatsWeightModifiers` reaches `Stat`. Vanilla weights enemy actions on stats only through ViolenceTracker and the autumn counter (all 27 vanilla Encounters scanned), none on the player's health, so the curve has no vanilla precedent. Patches asserted a byte-identical JSON round trip and every precondition before writing. Numstat: Thorne and Corrin +2099/-33 each (the 33 are the removed arrest-flag entry), Sterling and Converge +2099/-0, Vane +6/-10; `SimpEn.csv` and `SimpCn.csv` +10/-0 each by binary append. `dotnet build -c Release` 0 warnings 0 errors; `Check-LocalizationParity.ps1` CMC 2526/2526 CLEAN; the checklist's TRACKS and ITEMS evaluate under node (181 items, 181 unique slugs, 0 missing fields).

### 2026-09-26 - Audit_Remediation_Plan
- **Verdict:** not implemented when checked. The plan held 0 fix rows (none promotable since 2026-09-20) and three feature rows, N1-N3, all still unbuilt, as the 2026-09-25 reconciliation had found. Built this pass as CMC 1.68.46: N1 Ochre and Billberry Pigment plus 18 new dyed garments (seven garments in three colours, each with its own art); N2 Herb Paste flavour (Pungent, a new `cmcflavourtannic`, Herbal), a Season CI onto 19 vanilla cooked meat and fish cards, and one FlavourMatrix pair (Tannic + Savoury, Good); N3 a pastime DA on each toy and a 'Play with the Toy' CI on both tamed cats (Care +20, toy kept).
- **Premise corrections (the rows were not built as worded):** N2 named Wild Garlic and the Inn stock, which are vanilla cards, and Silverwort and Hollow Sage, which have no eat action, so Herb Paste is the one CMC edible it fits; and a pair between two vanilla tags would be inert, because vanilla's matrix already scores all 136 pairs of its 17 tags. N3 counted three cats (there are two, `cmcInnCat` and `cmcShadowCatTamed`), credited the Cat Bed with Care (it gives Comfort +10), and specified `ReceivingCardChanges.ModType: 3` for the play action, which is Destroy and would have removed the cat.
- **Disposition:** ARCHIVE, appended as a second generation to `Documentation/Design/CMC_Audit_Remediation_As_Built.md` (plan verbatim, including the two DG1 notes of 2026-09-26) and removed from `Documentation/Plans/Community_Mod_Chest/`. The source rows are stamped SHIPPED in `Documentation/Ideas/Community_Mod_Chest/IDEAS.md` and `.audit/ideas.md` so `/audit-to-plan` cannot re-promote them; the Phase 2 pigment/flavour section and the Phase 4 toys row left this roadmap.
- **Pruned:** the whole plan doc, archived rather than dropped.
- **Verification debt filed:** `T2.285` (pigments and dyes, including the Weaver's second pigment), `T2.286` (seasoning, the Gratification gain and the too-strong limit), `T2.287` (toy pastimes and cat play), all tier B, pending, with no harness scenario yet.
- **Evidence:** Release build 0 warnings 0 errors, `bin/Release` carrying `FlavourTag/`, `FlavourMatrix/` and the 23 new pictures (csproj Content includes added for both folders); 16 Pester files, 735 checks, green before the docs pass, and the full gate is named in the retiring commit. The N2 numbers come from the decompiled flavour rules: a seasoned plain Cooked Lean Fish scores 2.0 (no Gratification) without the Tannic + Savoury pair and 3.0 (+25) with it, and four coats put Pungent at 8 against its limit of 6.

### 2026-09-26 - Content_Update_Plan (CMC 1.69.0, plan closed)
- **Verdict:** all 7 selected rows shipped in CMC 1.69.0 "Good Neighbours", committed together in
  `1c6280bd1`; 0 rows left buildable, and no prompt pack existed. 0a: the Armorer course listed
  `act_bp_iron_breastplate`, which matches no blueprint, and now lists both real iron breastplates
  (`_f`, `_m`) in `Patcher/AcademyCourseService.cs`. 1: Chat writes +1 trust and every trade 0.08 trust
  per trade value on the Miller, Weaver and Apothecary; trust scales their prices; the Apothecary's
  board shows her friendship line. 2: 'Give Bread' (Miller) and 'Give Painted Clay' (Weaver), +3 trust,
  96-tick cooldown, no errand stat touched. 3: all five residents mark prices up while Wanted and keep
  that maximum while Banished (owner call, below). 4 and 5: `Patcher/InnEveningPatch.cs` with `CardData/Location/CMC_InnCounter.json`.
  6: `Patcher/FugitiveWatchPatch.cs`.
- **Departures from the plan text, each deliberate:** feature 6 raises Village Crime TO 12 instead of
  calling `AddCrime(12f)`, so a Fugitive who already carries crime is never pushed past Suspected (the
  plan's own risk register asks for that); a crime already above 12 is left alone and the latch still
  sets. Chat trust landed as a JSON `NPCStatModifications` write, so the plan's fallback handler was not
  needed. Open questions 1-8 took their written defaults except question 4, the owner-only tone
  call: 1.69.0 was first built with Banished residents refusing Trade (the plan's default, in `1c6280bd1`),
  and before release the owner chose a steep markup instead (answer "Steep markup instead", asked
  2026-09-26), so the crime `TradingConditions` entries were removed and the clamped Wanted maximum
  carries through crime 60-100. The 1.69.0 CHANGELOG entry also corrects 1.68.42,
  which listed chatting as a trust source when Chat wrote nothing.
- **Disposition:** ARCHIVE to `Documentation/Design/CMC_Content_Update_1_69_0_As_Built.md` (plan text
  unchanged below an as-built header) and removed from `Documentation/Plans/Community_Mod_Chest/`, per
  `Documentation/Plans/README.md` step 4. Its "Considered, not selected" table stays there as the record
  of why each other backlog row waited, and `Documentation/Ideas/Community_Mod_Chest/IDEAS.md` points at it.
- **Pruned:** the whole plan doc, archived rather than dropped; this file's Phase 3 rows "Gift-preference
  friendship for the Miller and Weaver" and "Give the `Fugitive` trait a hook into the Watch" (both
  shipped). Dated resolution lines went into `.audit/ideas.md` for the matching two entries, and the six
  source rows in IDEAS.md are annotated with what shipped and which half stays open.
- **Verification debt filed:** `T2.300` (Armorer breastplates), `T2.301` (chat and trade trust, prices;
  human half: one real trade and the Apothecary's own Chat), `T2.302` (daily gifts), `T2.303` (crime at
  the counter; human half: the Apothecary), `T2.304` (Join the Table), `T2.305` (Play for the Room),
  `T2.306` (Fugitive; human half: the twelve-day fade). Each has a walkthrough under
  `TestHarness/client/scenarios/CMC/`. None had run when this was filed: 1.69.0 was not yet deployed and
  the game was held by another session's runs.
- **Evidence:** `Development_Tools/Tests/CMC-ResidentFriendship.Tests.ps1`, 11 checks, 9 of them red on
  the pre-1.69.0 files (the other 2 are invariants that already held), and the new UID-resolution check
  in `Academy-CourseGating.Tests.ps1`, red on the pre-fix source in a scratch fixture. Full gate before
  the commit: 1912 passed, 6 failed, each failure attributed to another in-flight release (stale MUM
  suite embeds, HarshWilds' `hw_lynx_carcass` capacity, other releases' open rows and citations) and none
  to this change once the checklist mirrors landed. The seven walkthroughs passed run_walkthrough's
  dry-run validation only, which checks their shape, not the game.
- **Follow-up, same day (harness pass, CMC 1.69.1):** 1.69.0 was deployed and the seven walkthroughs run
  on the live game (EA 0.68b, framework 2.27.0). They found two CMC defects, fixed in 1.69.1
  (`fcad1d684`): `CottageResidentSchedulePatch` cached its Inn, Academy and cottage EnvIDs at the main
  menu, when mod env cards still read `UniqueIDIndex` 0, so the Miller and Weaver never left home for the
  Inn or the Academy (probe: cached `EnvIndex` 0 against the card's live 7; this also failed T4.40); and
  `QuestChainSchedulePatch` held the trust mirror behind the first-week gate. On 1.69.1 all seven rows
  pass. Recorded in `06a19be4e`: T2.300, T2.302, T2.304 and T2.305 confirmed; T2.301, T2.303 and T2.306
  keep only the human halves named above. The MUM suite embed still carries CMC 1.68.45: the next
  `/package-mod-suite`, the release step of whichever in-flight suite release lands last, picks up 1.69.1
  from `bin/Release`.

### 2026-10-03 - Community_Mod_Chest round 2 (/audit-plan)
- **Verdict:** 72 selected round-2 features (47 phases A-H, 4 phase I, 21 phase J) plus the Wave 0 research.
  This pass pruned the 21 BUILT features of Phases A, B, C and J1 (A1-A5, B1-B5, C1-C10, J1), all committed in
  CMC 1.69.3 and confirmed on disk. 20 Selected-table rows were removed (A1-A5, B1-B4, C1-C10, J1; B5 was a
  build-discovered follow-up never listed there), leaving 52 rows: 28 in phases D-H, 4 in phase I, 20 in
  phase J. The pack dropped from 59 open prompts to 45. Still open: Phases D to J (J1 excepted) and the four
  live-game Wave 0 reads W0.12, W0.14, W0.21 and W0.22 (pack Prompt 1).
- **Disposition:** KEEP IN PLACE. Phases D to J are genuine unbuilt code work, so the plan stays in
  `Documentation/Plans/Community_Mod_Chest/`; only the built feature sections, their Selected-table rows and
  their pack prompts were pruned, per `Documentation/Plans/README.md` and the pack's prune discipline. Not
  DELETE and not ARCHIVE.
- **Evidence:** Ground-truthed every built claim against current CMC source (read-only subagent sweep):
  all 21 features present on disk - A1 `Patcher/WildlifeIndoorGuardPatch.cs` (a `MoveNPC` prefix) and
  `EncounterGuards/CMC_VillageNoWildlife.json`; A2 the `cmcStatStallRobberyDays` GameStat; A4
  `Bp_CeramicLamp.json`; A5 `CharacterPerk/Pk_GradCarpentry.json` PerkIconWarpData `VillageAcademyCarpentry`
  plus its PNG; B1 the five `Agent_*.json` carrying the Socials and FacialHarmony GUIDs; B2
  `Patcher/VillagePricing.cs`; B3 ten crime-band DialogLines; B4 `Patcher/LuckBonus.cs`
  (`0.05 * Clamp01(luck / 36)`); B5 the Interlock drags and Friendship_Warm answers; C1
  `GameStat/CMC_ApothecaryGiftCooldown.json`; C2 three keepsake lines; C3 thirteen remark lines; C4 five
  overheard lines; C5 `CurrentLegLabel`; C6 `Patcher/RoadMeetingPatch.cs`; C7 `Patcher/CommissionPatch.cs`;
  C8 the stall-portrait branch plus its PNG; C9 `InnKeeperRestock_HF`; C10 `WeaverRestock_Sirus`; J1
  `InnKeeperRestock_ACT`. Version 1.69.3 in `ModInfo.json`, `Plugin.cs:30` and `README.md:3`. Tracker rows
  T2.900-T2.924 all present in `.claude/playthrough-test-status.json` (Phases A/B a mix of pass and pending,
  Phase C all pending). Document edits measured by `git diff --numstat`: `MASTER_PLAN.md` +19/-502 (deleted
  the Phase A, B and C sections and J1, pruned the 20 Selected-table rows, updated the tally, replaced three
  handoff blocks with one, appended a dated status paragraph, fixed forward-pointers to the built features);
  `MASTER_PLAN_Implementation_Prompts.md` +69/-333 (removed the former Prompts 2-15 and the
  Awaiting-verification list, renumbered the surviving 45 prompts to 1..45 with deps remapped, rewrote the
  header to `45 open / 0 awaiting verification` against `57f4299c8`). Post-edit checks: the plan has 0
  deleted-section headings left; the pack's headers run 1..45 (grep count 45) with 90 `----------` separators
  matching 90 code fences and no body prompt-reference above 45; the added-count of U+2014 is 0 in both
  files; both files are LF with no CRLF.

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 11 plan features checked (I3, J7 to J11, J15, J18 to J21) plus 4 live-game Wave 0 reads (W0.12, W0.14, W0.21, W0.22) and 8 pack prompts; 0 verified done this pass, 8 built-awaiting-verification features recorded as pointers in the pack (J2, J3, J5, J6, J12, J13, J14, J16 on T2.981 to T2.988), 19 built features with a failed or pending harness row (T2.925 to T2.978, pack "Failed" list), 11 open. This is the second pass the same day, after the prune at `d450c68c3`.
- **Disposition:** KEEP IN PLACE. 8 open prompts remain.
- **Pruned:** nothing new. The pack header's awaiting-verification count was corrected from 6 to 8 features, and one dated status paragraph was appended to the plan. The built-feature sections were already deleted at `d450c68c3`.
- **Evidence:** `git log d450c68c3..HEAD` over the mod and plan: 5 commits, none touching mod source (only `bin/Release` artifacts, the merge, and a cloud pack refresh already folded in); plan and pack diff since `d450c68c3` is +6/-4 in the pack only (prompt wording). Grep of `Community_Mod_Chest/` (json, cs, csv, excluding bin) for the 11 unbuilt features' markers (pescatarian, wool-quilted, filled-vase UID, `tag_x_WaterSource`, limed, King of the Forest, `CMC_TannicEarthy`) returned 0 files; control: the same grep shape for `tag_Structure` hit 2 files, and `cmcimpwell` read directly shows `CardTagsWarpData` `["tag_Structure"]` only and no duty GUID `66d4a21f` in `CardData`. `Documentation/Research/Community_Mod_Chest/harness_measurements.md` does not exist (directory listing: 4 other files), so Prompt 1 has not run. Built-awaiting features spot-checked on disk (odd-jobs `Patcher/InnOddJobPatch.cs`, `CMC_Diploma*.json`, `CMC_ForagingForestWildlife.json`, `Bp_CMC_PeatHut.json`, `Imp_PeatRoof.json`, `Bp_CMC_WeaverProcessHemp.json`); all 31 commit hashes cited in the pack and plan resolve with `git cat-file -t`. Script over the tracker (552 rows): every `T\d+\.\d+` in the plan (19) and pack (33) exists; no U+2014 and no CRLF in either file. The "fix in flight (csff-mods-15 uncommitted working tree)" lines are not verifiable from this checkout (git status shows no uncommitted CMC source), kept as written.
- **Verification debt:** pending: T2.900, T2.924 to T2.927, T2.929, T2.932 to T2.935, T2.938 to T2.940, T2.943, T2.945, T2.954 to T2.956, T2.959, T2.961, T2.965, T2.967, T2.968, T2.978, T2.980 to T2.988, plus T2.237, T2.285, T2.287, T2.301, T2.303, T2.306 cited by Considered rows; failed: T2.601 (WDI, cited for W0.21); skipped: T4.35.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** counts: 9 plan items checked (I3, J7, J9, J15, J18, J19, J20, W0.12, W0.22) plus open questions 15, 22 and 27, the risk register and 3 pack prompts; 0 verified done by a passing tracker row in the plan itself (T2.988, J14, was confirmed and left the pack's list), 6 built awaiting verification (J7 T2.994, J9 T2.995, J15 T2.990, J18 T2.989, J19 T2.991, J20 T2.992), 3 open (I3 behind W0.12, the W0.22 reading, the closeout), 2 obsolete (open question 22, answered by J7's build; the J21 risk row, J21 closed), 3 stale claims corrected (the Next up section, the Phase J intro, the 2026-10-06 validation note's suspected T2.982 defect), 0 failed in the tracker (T2.932 and T2.978 failed on harness scenarios, unrecorded; kept in the pack's Failed list).
- **Disposition:** KEEP IN PLACE - 3 open rows of code work remain (pack Prompts 1, 2 and 6: W0.12/W0.22 research, I3, and the closeout's art, scenarios and `Features.json` rows).
- **Pruned:** plan: open question 22 and the J21 risk row deleted; the J7, J9, J15, J18, J19 and J20 as-built paragraphs collapsed to one-line pointers (their checks live in the pack's Awaiting verification list); the 2026-10-02 to 2026-10-06 status paragraphs condensed to one history line (full text at `a11a1fd6f`). Pack: the J14 bullet (T2.988 confirmed), the closed-J8/J10/J11/J21 tail, and Prompt 6 step 1's reference to the removed Prompts 3 and 4; header history condensed and a dated status paragraph added.
- **Evidence:** `git log 695529772..HEAD` over `Community_Mod_Chest`, its plan, research and scenario folders: 2 commits, neither touching mod source (`3d0734589` plan prune, `72273e90f` rows); `git cat-file -e HEAD:` found all 7 built files (`PaintedClayVaseFlowers.json`, `CMC_LimewaterTrough.json`, `Bp_CMC_LimewaterTrough.json`, `CMC_KingOfTheForest.json`, `CMC_WoolQuiltedCap.json`, `CMC_WoolQuiltedVest.json`, `CMC_TannicEarthy.json`); `git grep` at HEAD: `tag_x_WaterSource` on `CMC_VillageWell.json:18`, "Arrange Flowers" on `PaintedClayVase.json:79`, and "pescatarian" in 0 mod source files (control: the same grep hits `ROADMAP.md`), so I3 is unbuilt; `Documentation/Research/Community_Mod_Chest/` lists 4 files and no `harness_measurements.md`. Tracker script over items/confirmedItems: T2.988 in confirmedItems, the other 22 cited rows pending. Pack-coverage check: every OPEN plan row maps to a pack prompt (W0.12 and W0.22 to Prompt 1, I3 to Prompt 2, the release tail and the built features' art, scenarios and `Features.json` rows to Prompt 6); open question 27 is an owner decision, not code. Harness reports dated 2026-10-07 on disk (unrecorded): 13 of this plan's rows PASS, T2.932, T2.978, T2.982, T2.984 and T2.986 FAIL (T2.982 only at its log-line count step; T2.986 on the missing hut art).
- **Verification debt:** pending: T2.925, T2.926, T2.929, T2.933 to T2.935, T2.943, T2.967, T2.968, T2.981 to T2.987, T2.989 to T2.992, T2.994, T2.995; harness-failed (scenario defects): T2.932, T2.978.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** counts: 9 plan items checked (I3, J7, J9, J15, J18, J19, J20, W0.12, W0.22) plus open questions 15 and 27, the Considered table's tracker citations and 4 pack entries (3 prompts, 1 decision); 5 verified done since the last pass and removed from the pack (T2.929 D4, T2.933 D7 loaded dice, T2.943 F1, T2.982 J3, T2.978 I4, all now in confirmedItems), 10 rows built awaiting verification (9 with an unrecorded 2026-10-09 harness PASS), 7 failed on the 2026-10-09 harness run (T2.932, T2.986, T2.989, T2.990, T2.991, T2.994, T2.995; tracker still pending), 4 open prompts (W0.12/W0.22 research, I3, one new scenario-fix and diagnosis prompt, the closeout), 0 obsolete, 4 stale claims corrected (version 1.69.3 to 1.69.4 in three places, the T2.285 and T2.237 states in two Considered rows, J18's art).
- **Disposition:** KEEP IN PLACE - code work remains: pack Prompts 1 to 4 (W0.12/W0.22 live-game reads, I3, the failing scenarios and three diagnoses, the closeout's eight missing card images and `Features.json` rows).
- **Pruned:** plan: no section deleted; the J7, J9, J15, J18 and J19 pointers re-aimed at the pack's Failed list, the Next up section rewritten for 2026-10-09 and one dated status paragraph added. Pack: the D1/D2/D4/D7/F1, J3 and J7/J9 awaiting bullets as written on 2026-10-07 (rows confirmed, or moved to the Failed list), the I4 Failed entry (T2.978 confirmed), and the old Prompt 6's scenario step (every row's scenario exists, written in `48b0a0c1c` and `2652a05af`) and its J18 art (resolved by `2d9e0cccc`); Prompt 6 renumbered 4; new Prompt 3 added under refresh Step R3 for the harness failures.
- **Evidence:** `git log a11a1fd6f..HEAD` over `Community_Mod_Chest`, its plan, research and CMC scenarios: 8 commits, one touching mod source (`2d9e0cccc`, 9 files, art names and version only). Deployed `Community_Mod_Chest.dll` md5 a68e6e7f... equals `bin/Release` (deployed 2026-10-06, ModInfo 1.69.3). Tracker script over items and confirmedItems for 41 cited ids: 0 not found; the 10 awaiting and 7 failed rows all read pending. Latest-report script over `TestHarness/client/reports/t2-<id>-*` for 17 rows: 9 PASS and 7 FAIL on 2026-10-09 runs, T2.993 (not this plan) FAIL. File probe for the 10 art names in `Community_Mod_Chest/Resource/Picture/`: 0 present (control: the same folder lists 248 PNGs, so the probe can hit). `grep -ril pescatarian` over mod source (json, cs, csv, excluding bin): 0 files (control: the same grep finds this ROADMAP); `harness_measurements.md` absent from `Documentation/Research/Community_Mod_Chest/` (4 files listed). Scenario listing: 30 CMC files for T2.932 to T2.996.
- **Verification debt:** pending, harness PASS awaiting record: T2.934, T2.935, T2.968, T2.981, T2.983 (with-DurosCoinage half not run), T2.984, T2.985, T2.987, T2.992; person: T2.1323; harness-failed, tracker pending: T2.932, T2.986, T2.989, T2.990, T2.991, T2.994, T2.995; skipped, cited by Considered rows: T4.35.

### 2026-10-09 - MASTER_PLAN (owner removal of J12 and J13)
- **Verdict:** counts: 2 built features removed by owner decision (J12 the Peat Hut, J13 the Peat Roof); 0 other plan rows touched. Owner, 2026-10-09: "We want to revert the peat hut this is too redundant with the vanilla mud hut please remove all remnants of the peat hut and its components".
- **Disposition:** KEEP IN PLACE - code work remains (pack Prompts 1 to 4). J12 and J13 moved to the plan's Considered table under "Removed by an owner decision after shipping" with the reason.
- **Pruned:** pack: the J13 awaiting bullet, the J12 failed bullet and the three peat art names in Prompt 4 step 1; plan: a dated removal paragraph and the Considered row. Mod: the six peat card files, their 15 localization keys in both CSVs, the House Building tab entry, six InjectImprovementInto rows and the three interior-list entries (CMC 1.69.5).
- **Evidence:** CMC 1.69.5 commit; `Development_Tools/Tests/CMC-Round2.Tests.ps1` J12/J13 Context rewritten to assert the removal holds.
- **Verification debt:** none added; T2.986 and T2.987 moved to the tracker's droppedItems.

### 2026-10-09 (later) - MASTER_PLAN, second /cleanup-plans pass
- **Verdict:** NOT drained. 4 open prompts (1 W0.12/W0.22 live reads, 2 I3 pescatarian perk, 3 T2.994 diagnosis, 4 closeout), 13 awaiting verification, 1 failed (J7 / T2.994). Also open in the plan: five missing images and Features.json rows for round-2 features.
- **Disposition:** KEEP IN PLACE - code work remains.
- **Pruned:** X1 (Copper Bed Frame text, shipped f8d1f8bac) collapsed to a pointer (cited by the framework plan); ACT hard-dependency claims in section 0 and Guardrails marked superseded (a3f965d43); J9, J15, J18, J19 moved from Failed to re-run owed (scenario rework 618ef7c78); Prompt 4's stale T2.986 line removed (Peat Hut removed in 73705a83a).
- **Evidence:** audited against 1d9cbf121. Plugin.cs:10 declares only the framework; X1 text in Bp_CopperBedFrame.json and both CSVs; no pescatarian perk in CMC and no harness_measurements.md; T2.932 PASS report t2-932-*-20261009-162110. Gates PlaythroughRow-Citations 13/13, Plans-AwaitingRowIds 5/5, Plans-Policy 12/12.
- **Verification debt:** T2.981, T2.983, T2.985 (PASS, unrecorded); T2.968, T2.984 (PASS, FIX-SCENARIO at the 2026-10-09 second review; T2.932, T2.934, T2.935 and T2.992 recorded CONFIRMED the same day); T2.989, T2.990, T2.991, T2.995 (re-run owed); T2.994 (failed); T2.1323 (person).

### 2026-10-11 - MASTER_PLAN (round 2), /cleanup-plans apply
- **Verdict:** DRAINED. 0 open prompts, 0 unbuilt plan rows (J22 built in `155ec13f1`), 13 awaiting rows recorded confirmed (T2.968, T2.981, T2.983, T2.984, T2.985, T2.989, T2.990, T2.991, T2.994, T2.995, T2.1450, T2.1451, T2.1452); open question 27 keeps its as-built default.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/CMC_Round2_As_Built.md` with its drained prompt pack appended verbatim, and the pack was deleted; it carries the Considered table and its rejection reasons.
- **Pruned:** plan sections J7, J9, J15, J18, J19 and J22 (each confirmed), open question 28 (decided by `155ec13f1`) and the "Next up" note; pack deleted (folded into the archive).
- **Evidence:** CMC 1.69.6 deployed to dev rig 3 (`Deploy-Mods.ps1 -GamePath`), harness batches `walkthrough-20261010-212506` and the per-row runs after it; two independent skeptic reads per row in two rounds (`.claude/handoffs/cmc-review-2026-10-10/`), whose fixes changed seven scenarios before recording; `autoplay.py record --apply` from a clean worktree (`a1f9881f6`, `1a4b30969`). T2.991's comparative clause refuted by measurement (seasoned meat synergy 3.75, 3.5 with the pair switched off, fish 3), docs reworded `5744f22b4`; T2.1452 watched red on the pre-`155ec13f1` build. False Potter warning fixed `fa383d7c3`.
- **Verification debt:** T2.1323 (the Potter's art, a person row) stays pending. Release not done: CMC 1.69.6 is deployed to rig 3 only, not the real install, MUM is not repacked and Nexus holds 1.68.45.

### 2026-10-11 - Fleet MASTER_PLAN (cross-mod decoupling), /cleanup-plans Fleet apply
- Scope: Fleet (cross-mod plan; the same entry is recorded in the ROADMAP of every mod it names: Community_Mod_Chest, AdvancedCopperTools, WaterDrivenInfrastructure, HomesteadPerks, HerbsAndFungi, Sirus23_Mod_Collection, CSFFModFramework).
- **Verdict:** DRAINED. Features 1-13 all built (1-3, 6, 7, 9, 12 in the commits `33ab18fe9`, `a3f965d43`, `76aac3d8a`, `cc0115b9e`, `fed7f7d0a`, `c6b2bad35`, `31034e212`, `e7b558ead`, `7219cd11d`), 0 open pack prompts, 0 unbuilt rows; every acceptance row recorded CONFIRMED (T2.789, T2.632, T2.997, T2.999, T2.998, T2.549, T2.557, T2.560, T2.1236 to T2.1242). Open question 4 settled by measurement, not by an owner choice.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Fleet_Cross_Mod_Decoupling_As_Built.md` with its Considered table and its drained prompt pack appended; the pack was deleted.
- **Pruned:** plan sections 1, 2, 3, 6, 7, 9 and 12 (each BUILT and confirmed), the stale "awaiting verification" pointers, open question 4's default text (replaced by its measured outcome); pack Prompts 9 and 10 (Prompt 10 had already been built in `e7b558ead`, the pack was stale on that point).
- **Evidence:** on dev lane rig 6 at HEAD (H&F, Harsh Wilds and Sirus23 deployed from the working tree, the rest synced from the real install), harness PASS for T2.557, T2.560, T2.632, T2.997 (both variants), T2.999 and T2.1236 to T2.1242; two independent skeptic reviews of all twelve rows (`.claude/handoffs/skeptic-review-2026-10-11-fleet/r-a.md`, `r-b.md`): 11 RECORD-PASS, and T2.560 FIX-SCENARIO (its brine leg dragged the non-draggable Liquid sub-card, so it passed on any build; fixed in the scenario and rerun PASS with a real refusal, "Dropping 'Pickle Brine' onto 'Wolf Companion' does nothing", beside the same container type with water running Give Water once). Three of the seven absent-partner pins had drifted since 2026-10-09 and were re-measured from real alone-boots (CMC 48 entries, five sprites now resolve; H&F plus twelve of its own map-node forward references; Harsh Wilds plus the Forest Lore `fl_mud_volcano` weight row; WDI now 0 lines), each entry checked against `Documentation/Interop_Registry.md`. End state: `grep BepInDependency(` over every `Plugin.cs` finds 13 files and none names anything but CSFFModFramework (the 13 matches are the control that the scan can hit). Open question 4 probe: with Sirus23 set aside the Wool-Quilted Cap and Vest each keep four requirement slots and the Wool one is null (measured), and a researched blueprint is left alone by `AcademyCourseService.HideWoolQuiltedIfNoWool` (read from code).
- **Verification debt:** none open. One unplayed fact, deliberately without a row: what a craft of an already-researched Wool-Quilted recipe asks for after Sirus23 is removed (the game reads a null slot as no requirement, the fleet audit's reading of the code); CMC's README states it as unplayed. Releases still owed per mod under its own version rules (`.claude/nexus-publish-status.json` against `ModInfo.json`, read 2026-10-11): framework 2.28.0 (Nexus holds 2.26.12), CMC 1.69.6 (1.68.45), ACT 1.18.1 (1.16.10), WDI 1.12.2 (1.11.3), HSP 1.3.1 (1.2.4), H&F 1.14.1 (1.13.7) and Sirus23 1.22.0 (1.21.6); MUM not repacked for this change.

### 2026-10-18 - MASTER_PLAN (Skylit Cave escape and vanilla balance), archived by hand on the /cleanup-plans ARCHIVE disposition
- **Verdict:** DRAINED. Pack: 24 of 24 prompts landed (Prompts 1-4 Wave 0 research, 5-13 Wave 1 build, 14-15 Wave 2 harness and timing, 16-24 Part II), 0 open; its 3 OPEN-DECISION lines all answered by the owner on 2026-10-18 (CMC-V-B4 "Leave as is", CMC-V-B9 "Move to grants", CMC-A-B12 "Halve to +24"). Plan: Part I sections 1.3-1.8 built, plus two owner calls built after the closeout (Sterling's Salt "Bribe if suspected", the escape "Shorten toward 3-4 days"); Part II sections 2 and 3 built in 1.69.17 except CMC-A-B8 (ruled out, Considered table) and CMC-V-B4 (no change by design, now the owner's answer); section 6 N1-N6 all built, N1 as one chore of the three (the other two moved to the Considered table with their reason). 0 unbuilt rows.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/CMC_Skylit_Cave_And_Niche_As_Built.md` (git mv, rewritten as an as-built record with its two Considered tables and every rejection reason); the drained pack `MASTER_PLAN_Implementation_Prompts.md` was deleted. `Documentation/Plans/Community_Mod_Chest/` is now empty.
- **Pruned:** every prompt-shaped instruction in the plan (section 1.6 open-question defaults, 1.8 Waves 0-2, Part II section 4 owner-first list, section 7 build note, section 8 pointer, the 2026-10-15 status banner); the whole pack (Prompts 1-24 and the 3 OPEN-DECISION lines). The plan's own Execution Status journal is kept in the as-built record.
- **Evidence:** every prompt's landing commit is an ancestor of HEAD (checked with `git merge-base --is-ancestor`, 31 commits: Wave 0 `4082d5014` `ee9b20288` `d1bfb5bf2` `4a330bd3b`; Wave 1 `2604a0915` `4482f105d` `4635c93ba` `69e6e5c51` `0a4d4799f` `c5a4e463d` `f1f53df16` `e0b09bebc` `43f787871` `fe9c3b9ae` `cf0a07482`; closeout `6b1b25b5f`; Wave 2 `8e2ee0099` `4947a2835` `f6374213d`; Part II `fc7503360` `83f7e8100` `7c46bf00a` `3a22abd56` `ce2773158` `6bc370183` `adbf07cc6` `2764bb143` `6cbe3453e` `33618fbde` `80ddb1915`), and the post-closeout fixes and owner calls `77d22f098` `64b67dd83` `d2c46f2b1` `2763cb129` `249502074` `7c2d62315` `232d3e9bc` `aab4968c1` `c004180a2` `63b6fd676`. Escape measured with `TestHarness/client/bench_cmc_escape_timing.py`: 5.4 to 7.1 in-game days on the first tuning (`f6374213d`), 3.5 to 4.8 over four runs after the retune (`232d3e9bc`, raw files under `Documentation/Research/Community_Mod_Chest/escape_timing_2026-10-18*`). Gates as recorded by the builders: the full CMC Pester set 1143 passed at `80ddb1915`; harness PASS on the final DLL in rig 2 for 16 rows (`85a7d08d9`: T2.947, T2.1452, T2.904, T2.1461, T2.1462, T2.1464-T2.1468, T2.1469-T2.1472, T4.26, T2.1735). Tracker ids re-read in `.claude/playthrough-test-status.json` by their `id` field (a first read keyed on the row name found none; the control T2.1456 was then found by grep and every id below resolved).
- **Verification debt:** pending, a human in a window: T2.1454 (cave and ledge read right), T2.1455 (ladder in the project panel), T2.1456 (full escape length judged), T2.1457 (Professor's nuggets smelt as copper), T2.1458 (winter scarcity), T2.1459 (net through the Ice Hole), T2.1461 (escort), T2.1462 (amends) and its four per-resident halves T2.1469-T2.1472 (Weaver, Apothecary, Inn Keeper, Professor), T2.1463 (course teaches a skill), T2.919 (Apothecary's Road window), and the Part II rows T2.1726, T2.1727, T2.1731, T2.1733, T2.1735. Already confirmed: T2.941, T2.1460, T2.1464-T2.1468, T2.1724, T2.1725, T2.1728-T2.1730, T2.1732, T2.1734, T2.1736, T2.1737. Release not done: CMC 1.69.19 is built and harness-tested but not deployed to the real install or published (Nexus holds 1.69.18).
