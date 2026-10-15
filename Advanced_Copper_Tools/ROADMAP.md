# Roadmap: Advanced Copper Tools
Version at time of writing: 1.18.1
Date: 2026-10-11
Audit score: 9/10 - PASS (0 open CRITICAL; consolidated 2026-10-11)

> **Rewritten 2026-10-11 after a ground-truthed consolidation.** The standalone critical-analysis (2026-10-11)
> reads BROKEN on two criticals; both are cleared on current evidence. **A6** (build failure from a stale
> `lib/CSFFModFramework.dll`) is RESOLVED in the working tree - framework rebuilt, lib refreshed to the
> 2026-10-11 build, ACT rebuilt clean 0/0 (the fix is uncommitted: `GameLoadPatch.cs` + the DLL are dirty).
> **X1** (Render Hemp Seed Oil's foreign H&F UID "silently breaks" standalone) is a FALSE POSITIVE for its
> break severity: `Patcher/PartnerBlueprintGatePatch.cs:38` hides the recipe at runtime when H&F's hemp seed
> oil card is absent, via a `GameManager.InitializeStatsAndActions` postfix - the same proven HIDE pattern it
> uses for Copper Shears. The genuine residue is a doctrine-bookkeeping gap (the HIDE exception is unregistered
> in `Documentation/Interop_Registry.md`), carried as a Phase 1 item. The mod is on the release line; what
> remains is a short reliability/doctrine tail plus a large, human-gated in-game verification debt.

## Current State

**Theme**: Mid-game copper-and-bronze metalworking - a comfort/throughput/light tier layered on vanilla
metallurgy, fully multi-metal (copper -> ghost/tin/tin-bronze/white bronze -> iron) with SD4 metal-type gating
end to end, fed by vanilla ore sources plus Stream Tin panning (its own cave system was removed in 1.18.9).

**Content**: 74 items / 64 blueprints / 29 structures (CT2) / 7 perks / 150 custom images / 1 WorldMap (snapshot at 1.18.1; 1.18.9 removed the WorldMap and the cave cards).

**Stability**: 9/10 PASS - 0 open CRITICAL, 0 Design Gap, 3 WARNING (2 reliability, 1 interop doctrine),
2 MINOR. Builds clean after the 2026-10-11 lib refresh; code-quality 10/10.

**Open work**: no open ACT retrospectives (all Graduated or archived-with-pointer). Framework-wide
`wikimod-old-save-load-crash` is Pending Verification and applies to every DATA mod, not to ACT code.

**Framework compliance**: Tier 2 - uses `Api.*` / `ActionRouter` / `SpawnService`-class services across
~7 patchers (`GameLoadPatch`, `HeatHeldLiquidPatch`, `TinOreSmeltPatch`,
`SawEffectPatch`, `TeaStationPatch`, `VanillaFireKettlePatch`, `PartnerBlueprintGatePatch`). No deprecated
patterns (`DropCollectionGuardPatch` / `InitializeNullFields` / `PatchAll()`). Versions synced 1.18.1 x3.
Chinese localization present (`SimpCn.csv`, parity clean).

---

## Phase 0: Stabilize  *(near-done - one commit outstanding)*

> The A6 build fix exists in the working tree but is uncommitted.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Commit the A6 fix (framework `lib/CSFFModFramework.dll` refresh to the 2026-10-11 build + the `GameLoadPatch.cs` rebuild) so the clean build is durable, not just local. Verify the resolved binding with `dotnet build -v:d \| grep "Resolved file path"`. | Build / CRITICAL fix | P0 | Quick |

---

## Phase 1: Foundation

> Cheap reliability + doc-honesty cleanup; do next.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| `GameLoadPatch.cs:246,272` - replace `AccessTools.TypeByName("GameManager")` with `CardUtil.FindGameType("GameManager")`, matching `PartnerBlueprintGatePatch.cs:55`. ModCore can shadow the type and silently disable the save-load half of armor repair. (summary W1 / code-quality B) | Reliability | P1 | Quick |
| `GameLoadPatch.cs:382` - add a per-cause `WarnArmorRepairOnce` when the `UpdatePassiveEffects` reflection lookup returns null, so a game-update rename is not a silent no-op. (summary W2 / code-quality G5) | Reliability | P2 | Quick |
| Register the Render Hemp Seed Oil HIDE exception (structural `herbs_fungi_hemp_seed_oil` UID) in `Documentation/Interop_Registry.md`, OR route the hemp ingredient through an interop tag/ALT. Closes the one legitimate residue of critical-analysis X1 so the next adversarial pass does not re-flag it. (summary W3) | Interop doctrine | P2 | Quick |

---

## Phase 2: Core Expansion

> The 1-2 most natural next content additions. Both carried from the archived MASTER_PLAN's Long-term Vision.

### Copper Ore Crusher / secondary smelting yield
**What**: a placed crusher (or drag-hammer CI on the stove/station) turning raw ore into a distinct
"crushed ore" item that smelts at a better nugget yield.
**Why**: the keystone that turns raw ore (vanilla greenstone and bog iron, panned tin ore) from a flat drop into a processing chain the mining loop
already feeds; named as the mod's next major addition.
**Requires**: crushed ore MUST be its own item with its OWN smelting entry - never a second recipe on the
same item (CLAUDE.md Smelting, the WDI 48-vs-12 copper bug). Decide standalone-structure vs CI, and the
yield delta, first.
**Complexity**: Complex.

### Metal Alloying step (copper + tin -> bronze nugget)
**What**: an explicit alloy recipe with copper-only lockouts, feeding the now-live SD4 sheet gate.
**Why**: the SD4 ladder reaches the sheet and Bronze gear reads it, but there is no explicit alloy step -
this closes the ladder's bottom rung (bronze nuggets come only from the vanilla crucible today).
**Requires**: a concrete design pass; do not silently eat tin nuggets.
**Complexity**: Complex.

> The Metal Sieve was CONSIDERED and ruled out; its rejection reason lives in the archived
> `Documentation/Design/AdvancedCopperTools_Content_Update_As_Built.md` Considered table - do not re-promote it.

---

## Phase 3: Integration & Depth

### Proximity warmth from lit placed heat sources
**What**: a passive Body-Temperature buff while standing near a lit ACT stove / brazier / bathtub.
**Why**: makes the three placed heat sources meaningfully better than a plain campfire and ties them into
one comfort system; today their heat reaches only held liquids.
**Requires**: investigate whether vanilla exposes a proximity-warmth hook before designing
(`CSFF_Patterns.md` light + fuel).
**Complexity**: Medium.

### Cave-lantern utility (retired)
2026-10-14: cave system removed (owner decision). ACT 1.18.9 dropped the five caves and the dark-mine mechanic, so this row has nothing left to build on; the Metal Lantern stays a plain light source.

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Iron lantern art (summary M1) | The four `ACT_MetalLantern_*_Iron` images ship unused until the iron lantern is built: owner 2026-10-13 chose to wire them, plan `Documentation/Plans/AdvancedCopperTools/MASTER_PLAN.md` section 1. (`CopperStoveTop`, `CopperStove_Placed_Lit` and `Copper_Lantern_Lit` were already deleted in `92f1ce420`.) | Medium |
| Verification-debt burn-down | Run the pending in-game rows (T2.750-T2.788, person rows T2.1310-T2.1315) through the playthrough tracker - the only gate between the mod and a 1.18.1 release. Human-gated, not code work. | Medium (human) |

---

## Long-term Vision

At v1.18+, ACT is the complete mid-game metallurgy layer: raw ore -> crushed ore -> alloyed nuggets ->
graded sheets -> a full copper/bronze/iron gear tree, sourced from vanilla ore plus Stream Tin panning and
interchangeable with WDI's water-driven hardware. The Ore Crusher is the keystone that turns raw ore
into a processing chain; the alloy step rounds out the "everything has an upgrade path" identity.

**Potential major additions** (not yet justified - revisit after Phase 3):
- Copper Forge / smithing-upgrade station - a hotter/faster ACT forge as the prerequisite tier for a future
  Ironworks; fits the "own end-to-end supply chain" theme.
- Copper Still / distillation - highest-risk, no vanilla template; must ship standalone ACT value so it is
  not dead content without H&F.

Both are ruled out, with their reasons, in `Documentation/Design/AdvancedCopperTools_Content_Update_As_Built.md`
(the archived MASTER_PLAN's Considered table). Anything worth reviving belongs back in a plan under
`Documentation/Plans/AdvancedCopperTools/`, never a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod AdvancedCopperTools` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp.dll`, check CLAUDE.md EA notes, `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis AdvancedCopperTools` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo AdvancedCopperTools` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod AdvancedCopperTools         - full health check, updates .audit/
/critical-analysis AdvancedCopperTools - adversarial review (re-run: its 2026-10-11 BROKEN verdict is superseded)
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
