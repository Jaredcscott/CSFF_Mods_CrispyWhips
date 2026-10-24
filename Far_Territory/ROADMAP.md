# Roadmap: Far Territory
Version at time of writing: 0.1.11
Date: 2026-10-23
Audit score: 10/10 - PASS (1 open WARNING + 1 open MINOR from the module audits)

## Current State

**Theme**: Lands beyond the forest, reached by ordinary travel - a tropical island (a tribute to Card Survival: Tropical Island), plus a mainland grassland, snowy mountains and cold fjords. Fishing, foraging, climbing, butchery, sea dangers and a castaway start, for a mid-to-late survival loop.

**Content**: 99 items / 5 blueprints / 167 location+structure cards / 1 perk (Island Start) / 2 liquids / 181 custom images

**Stability**: 10/10 - 0 CRITICAL, 0 design gaps. Two open items from the 2026-10-23 module audits: the fish/funnel trap descriptions overstate catch cadence ("every few hours" vs ~once a day), and the orphaned effect stat `ft_stat_ginger` is dead data. Both are low-risk polish. The one historical CRITICAL (Fish Trap never researchable) is re-verified RESOLVED.

**Open work**: No open or pending retrospective names Far Territory, `crispywhips.far_territory` or FT.

**Framework compliance**: Tier 2. C# is 4 small classes (`ClimateService`, `EnvSaveDataPreCreate`, `LandmarkSeed`, `OwlIslandGuard`) using `ContentModPlugin`, `SpawnService` and `TickEvents`; no deprecated patterns (no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no manual perk/blueprint injection, no `ModLoaderVerison`). Requires CSFFModFramework 2.28.2+.

---

## Phase 0: Stabilize

> Score is 10/10 with no open retrospectives, so the heavy stabilize phase is skipped. Two small loose ends remain from the module audits - both quick, neither blocks a release.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Reword fish/funnel trap "springs every few hours" to the real ~once-a-day cadence, in both `FT_FishTrapPlaced` and `FT_FunnelTrapPlaced` CardDescription CSV rows (SimpEn + SimpCn, all four) and the matching JSON `DefaultText` | Docs-honesty (WARNING W1) | P0 | Quick |
| Delete orphaned effect stat `ft_stat_ginger` (`GameStat/FT_Effect_Ginger.json` + `Far_Territory_Effect_Ginger_*` CSV rows), or repoint Ginger's Chew to it if a custom Ginger Warmth was intended | Dead-data cleanup (MINOR M1) | P1 | Quick |

---

## Phase 1: Foundation

> Table-stakes health. All green today; listed so a game-update or new-content pass re-verifies them.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Keep ModInfo/Plugin.cs/README versions in lockstep on every bump | Version hygiene | P1 | Quick |
| Chinese parity stays CLEAN (1962/1962 today) on every CSV edit | Localization | P1 | Quick |
| Re-seed the `modwide` module report (STALE stub) at the next full audit | Audit hygiene | P2 | Quick |

---

## Phase 2: Core Expansion

> The content additions the mod's own README already flags as unmet. Each closes a loop the mod half-built.

### Cooked shellfish and seaweed
**What**: A cook path (steam/boil over the base-game fire) for Conch, Oyster, Sea Urchin and Seaweed, mirroring the gutted-fish cook chain already shipped.
**Why**: README states "Shellfish and Seaweed have no cooked form yet" - the raw-only path is a visible gap next to cooked fish, yams and sago.
**Requires**: none.
**Complexity**: Medium

### Spice flavour synergy, confirmed
**What**: Verify (in game, or via a `FlavourMatrix/*.json` row) that Ginger/Chilli/Lemongrass/Jasmine actually flavour base-game dishes, and state the result in the README instead of "has not been checked in game".
**Why**: The spice plants are shipped and advertised; the payoff is currently unverified.
**Requires**: none.
**Complexity**: Quick-Medium

---

## Phase 3: Integration & Depth

> The four soft crossovers already ship. Depth here means tuning, not new wiring.

### Tuning pass on first-guess numbers
**What**: Revisit the self-declared "first guess" numbers once play data exists - cooked-food weights, the dried pith's 18 days, sea-salt boil time, fish-trap/net yields, cave forage tables.
**Why**: The README flags these as unbalanced placeholders; a data-driven pass is the natural next maintenance step.
**Requires**: in-game play data (the pending person-verification tracker rows).
**Complexity**: Medium

---

## Phase 4: Polish

> The active plan IS a polish-tier effort: adopting Card Survival: Tropical Island's own art.

| Item | What | Complexity |
|------|------|------------|
| CSTI art adoption (MASTER_PLAN) | Overwrite keyed item pictures with CSTI's own sprites; then the sky-key research and location/plant art | Complex (multi-phase) |
| Coconut Milk (optional) | A pressed-coconut liquid + a richer stew variant - only if it earns its weight | Medium |

---

## Long-term Vision

> At v0.2+, Far Territory is a feature-complete biome expansion: a tropical island, grassland, mountains and fjords that each feel distinct, reached by ordinary travel, with every advertised loop confirmed in real play and every card drawing art that matches the base game's hand. The CSTI art adoption is the single biggest in-flight addition; beyond it, the mod's natural endpoint is a tuning-and-confirmation pass that turns the many "first guess" numbers into balanced, playtested values.

**Potential major additions** (not yet justified - revisit after Phase 3):
- Cooked shellfish/seaweed chain - completes the raw-vs-cooked symmetry the mod already set up for fish.
- Coconut Milk and a milk stew - fits the coconut chain, but only if a second coconut sink is wanted.

Anything worth building belongs in `Documentation/Plans/FarTerritory/MASTER_PLAN.md` (or its Considered table, with the reason, if not); never a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod FarTerritory` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis FarTerritory` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo FarTerritory` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod FarTerritory         - full health check, updates .audit/
/critical-analysis FarTerritory - adversarial review
/repair-items FarTerritory      - auto-fix item JSON issues
/repair-blueprints FarTerritory - auto-fix blueprint JSON issues
/build-mod FarTerritory         - build Release DLL
/deploy-mods FarTerritory       - build + deploy to game
/update-mod-version FarTerritory <ver> - bump version in all 3 files
/export-to-repo FarTerritory    - push to public repo
```

## Plan Reconciliation Log

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 33 pack prompt numbers, of which 6 built and their text already pruned earlier (Prompts 1-6, VERIFIED-DONE on 2026-10-04 refresh), 4 built and awaiting verification (Prompts 7-10), 23 open (Prompts 11-33); 0 newly verified done; 0 obsolete. Plan: P0 and P1 done and pruned; P2, P3 and P4 built-awaiting; P5 split (part 1 built-awaiting, the rest open); every other selected phase open. Harness reports landed 2026-10-04 17:06-17:19 for all four awaiting items: T2.1300 PASS, T2.1301 FAIL (step 61 hang, W0-2 still unmeasured), T2.1302 FAIL (step 78, inferred scenario defect), T2.1303 FAIL (step 46, BodyTemperature 124.5 against the under-115 bound: the plan's Q5 branch, so the +30 island offset comes down). None is recorded in the tracker, so none counts as verified.
- **Disposition:** KEEP IN PLACE (23 open prompts remain).
- **Pruned:** nothing new was VERIFIED-DONE, so no phase or prompt was deleted. Corrected in the plan: the stale status opener "Queued. Nothing is built." and a duplicated "The owner's decision (2026-10-01)" heading; a dated audit paragraph appended to the plan's Status block. In the pack: the Refreshed header pin moved to `707bd32ca` and the four Awaiting verification entries gained the harness outcomes above.
- **Evidence:** `git log 3329a7e70..HEAD -- FarTerritory` printed no commit, so the shipped code is the state the 2026-10-04 refresh read; `git log` over `Documentation/Plans/FarTerritory` and `TestHarness/client/scenarios/FT` shows only tracker-row and scenario-rename commits after that refresh. Shipped files re-read: `WorldMap/MapNodes.json` (EnvironmentUID values `ftEnvRiverMouth`, the Duck Point clone, `ftEnvShore`, `ftEnvLandingBeach`, `ftEnvLandfallStrand`, `ftEnvCoralInlet`), `Patcher/EnvSaveDataPreCreate.cs` (the four non-clone env UIDs), `CardData/Location/FT_IslandClimate.json` (+30 `TemperaturePerceived` modifier, `AffectStatsOnlyWhenNotInBackground: true`, `AlwaysUpdate: false`), `CHANGELOG.md` `[0.1.0]` lines, `ModInfo.json` Version 0.1.0. Open prompts checked unbuilt: `ls FarTerritory/Animals FarTerritory/GameStat` both absent, and a grep of the mod's json and cs files (excluding bin and obj) for `ft_palm|ft_pop_|ft_canoe|ft_rope` returned no file; the control that the grep can hit: the same command shape over `ft_island_climate` matched 3 files. Harness verdicts read from `report.md` in `TestHarness/client/reports/ft-*-20261004-17*`. A script checked every `T\d+\.\d+` id in the plan and the pack (T2.1300 to T2.1303, 4 distinct) against `.claude/playthrough-test-status.json`: all 4 exist. Not verified: whether the DLL those runs deployed was HEAD's (`build.json` lists Far_Territory 0.1.0 with a hash that was not compared with `bin/Release`).
- **Verification debt:** T2.1300, T2.1301, T2.1302 and T2.1303, all pending; fix passes owed for T2.1302 (scenario check) and T2.1303 (Q5 offset), and the Partner half of T2.1301 (W0-2).

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** 0 claims newly verified done (no Far Territory file changed since `3705f3e48`); 2 obsolete pruned (the withdrawn W0-5 and W0-7 entries); 3 awaiting verification (P3 T2.1302 and P5 part 1 T2.1303, both harness PASS 2026-10-06 awaiting record plus a person half; P6 part 1 T2.1307, never run live); 3 failed (P4 T2.1301 at step 49, P5 part 2 T2.1305 at step 8, P20 T2.1306 at step 15, each a scenario defect whose fix sits uncommitted in the working tree; P4's older step-61 Partner stall remains); 3 stale claims corrected (branch `wave1/farterritory` called unmerged; framework section 30's Brackwater fix called owed although built in 2.28.0; section 7 listing Prompt 14's links as waiting); 20 open pack prompts (12, 13, 15-26, 28-33) plus the owner-gated P19.
- **Disposition:** KEEP IN PLACE - 20 open prompts of code work remain.
- **Pruned:** plan section 2's W0-5 and W0-7 bullets; Prompt 14's clause in section 7's "Links waiting"; the session handoff's "State at close", "Built and committed" and "Open, in order" (two load-bearing rules folded into its rules list); "Live-check findings" condensed to three short bullets in the same order (the T2.1304 scenario and row cite its second bullet). Pack: the Prompt 14 Awaiting bullet shortened (its as-built tunables moved to plan section 8), Prompts 11 and 27 moved from Awaiting to Failed, the stale branch note rewritten, Standing rule 18's taken range now T2.1300-T2.1307, header re-pinned to `189f8b99e`.
- **Evidence:** `git log 3705f3e48..HEAD -- FarTerritory/` printed no commit (the control: the same command over `Documentation/Plans/FarTerritory/` printed 4, `c6019390e`, `3881bd26a`, `68943d46f`, `72273e90f`); `git merge-base --is-ancestor` true for `ccf65bed4`, `962ede30d`, `3705f3e48`. On disk: 17 env UIDs in `Patcher/EnvSaveDataPreCreate.cs`, 18 nodes in `WorldMap/MapNodes.json` with Coral Inlet East, Rootwater Thicket South, Bleak Strand West and the Palm Fringe, Reed Marsh and Cliffbound Cove w=1 links; `FT_IslandClimate.json` +9 TemperaturePerceived and -480 SnowVisible; `CSFFModFramework/Injection/StripAllContaminationCheck.cs` present. Open prompts unbuilt: `CardData/` holds no `ft_palm`, uplands, cave or animal card and no `Animals/` or `GameStat/` folder exists. Harness verdicts read from `report.md` in `TestHarness/client/reports/ft-*-20261006-19*` and `-2003*` (2 PASS, 3 FAIL); uncommitted fixes seen by `git diff TestHarness/client/scenarios/FT/` (T2.1301, T2.1305, T2.1306). Tracker read: T2.1300 and T2.1304 in `confirmedItems`, T2.1301-T2.1303 and T2.1305-T2.1307 pending; every built item has a row, so none filed.
- **Verification debt:** T2.1301, T2.1302, T2.1303, T2.1305, T2.1306, T2.1307 (and framework T2.1232 for the Brackwater check).

### 2026-10-09 - MASTER_PLAN
- **Verdict:** 0 claims newly verified done and 0 obsolete (no Far Territory source commit since `3705f3e48`, and no FT row was among the 66 rows recorded at `aa60ef1b0` and `51641ef64`); 6 built prompts awaiting verification (8, 9, 10, 11, 14, 27), every one harness PASS on 2026-10-09 and unrecorded; 0 failed (the pack's three Failed entries, Prompts 8, 11 and 27, moved to Awaiting verification on those PASS runs); 3 stale claims corrected (W0-2 still "unmeasured on the real boat", W0-12's carried canoe "the rerun's", and P20's placed-CT2 fallback still live); 20 open pack prompts (12, 13, 15-26, 28-33) plus the owner-gated P19.
- **Disposition:** KEEP IN PLACE - 20 open prompts of code work remain.
- **Pruned:** plan section 2's W0-2 bullet (answered, now in "Wave 0 findings"); section 20's placed-CT2 fallback sentence; the "failed" labels on sections 6, 7 and 20 (now awaiting-verification pointers); a dated audit paragraph appended to the Status block. Pack: the "Failed - needs a fix pass" section removed and its three entries rewritten as Awaiting verification bullets with the 2026-10-09 reports; the Awaiting bullets for Prompts 9, 10 and 14 rewritten to the 2026-10-09 PASS runs and the T2.1324/T2.1325 person rows; header re-pinned to `51641ef64` with the next wave named. No commit was made by this pass.
- **Evidence:** `git log 189f8b99e..HEAD -- FarTerritory` printed no commit (the control: the same range over `TestHarness/client/scenarios/FT` printed 4, `48b0a0c1c`, `e57d44cfc`, `a47b5b8e3`, `dcb8d6cc7`); `md5sum` of `FarTerritory/bin/Release/Far_Territory.dll` and the deployed copy both `f8bccf67...`. Harness: the newest `report.md` per FT scenario under `TestHarness/client/reports/` reads `Result: PASS` for all six (crossing 20261009-074205, sea 055206, island climate 080714, shores 075042, interior 20261009-063000, canoe 072734); the prior day's shores and interior runs read FAIL (a swim-skill delta, a Coral Inlet charted read), fixed in the scenarios before the PASS. Tracker read: T2.1300 and T2.1304 confirmed; T2.1301-T2.1303, T2.1305-T2.1307, T2.1324 and T2.1325 pending. On disk: 17 env UIDs in `Patcher/EnvSaveDataPreCreate.cs` and 17 `ftEnv` CT4 cards; no `Animals/` or `GameStat/` folder and no uplands, cave, palm or `ft_pop_` card (the control that the grep hits: `ft_island_climate` matched the environment JSON).
- **Verification debt:** T2.1301, T2.1302, T2.1303, T2.1305, T2.1306, T2.1307 (harness PASS 2026-10-09, awaiting record), T2.1324 and T2.1325 (person, art), and framework T2.1232 for the Brackwater check.

### 2026-10-11 - climate cards removed (owner request)
- **Verdict:** no plan row opened or closed. Island Air, Mountain Air, Fjord Chill and Volcanic Heat (`ft_*_climate`, placed CT2 cards on every outdoor board) were reported not working and not wanted; they are deleted and `Patcher/ClimateService.cs` applies the same offsets (island +9 and SnowVisible -480, volcano +16 and -480, mountains -6, fjords -8, caves none) as a GlobalModifier from the current environment, reapplied from zero after a reload because `GlobalModifiedValue` is not saved.
- **Disposition:** KEEP IN PLACE. Plan section 1.3, the as-built bullets for P5, P6, P16 and P18 and Prompt 10 were reworded to say so; Prompt 34's lead about `FT_IslandClimate` now notes that card is gone.
- **Evidence:** build succeeded (0 warnings); `grep` for the four UIDs over `FarTerritory/CardData` returns nothing; the harness dry run accepts the new scenario T2.1530 and the 16 reworked FT scenarios. NOT yet run live.
- **Verification debt:** T2.1530 (new, harness) plus the reworked T2.1303, T2.1394, T2.1514 and T2.1516 climate checks; the older rows' acceptance text was rewritten 2026-10-11 to the card-free wording.

### 2026-10-12 - MASTER_PLAN closed (P19 built)
- **Verdict:** counts: 22 selected phases, every one built; P19 (distribution) built this day on the owner's Q7 answer, "Nexus + Mod_Repos". 0 open prompts; 16 tracker rows still await a person at a running game (T2.1324, T2.1325, T2.1391, T2.1393, T2.1398, T2.1501, T2.1503, T2.1509, T2.1510, T2.1513, T2.1515, T2.1518, T2.1521, T2.1522, T2.1523, T2.1524).
- **Disposition:** CLEARED. `Documentation/Plans/FarTerritory/MASTER_PLAN.md` moved to `Documentation/Design/FarTerritory_As_Built.md` with an archive banner; the drained pack was deleted and is that file's Appendix, whole. The Considered table and every Open question travel with it. Live citations (tracker sources, FT scenarios, the mod's Animals/Plugin/LandmarkSeed comments, research headers, the map table, `IDEAS_OVERVIEW`, Deploy-Mods comments) repointed; dated records left alone, and the Harsh Wilds plan (a peer's uncommitted edits) still cites the old path on line 276.
- **Evidence:** `2096a381b` (CHANGELOG `[Unreleased]` folded into a dated `[0.1.0]`; `LICENSE` and `ASSET_CREDITS.md` added and shipped by the csproj); build 0 warnings; gates Csproj-ContentIncludes, Deploy-Mods, CardArt-ShippedForm, Sprite-NameResolves, PackSuite-UnreleasedGuard, Nexus-Publish all green. Release zip `Far_Territory_0-1-0.zip` md5 `a6924a6b6cdeed126e66d019f4fe466e`; GitHub export pushed (`Mod_Repos` `968a137`, 723 files); Nexus upload replaced the hand-uploaded 2026-10-06 build on file slot 8094105 (`fba6c4924`); `nexus_publish.py status` reads published 0.1.0.
- **Left for the owner (website only):** paste the description (`nexus_publish.py describe FarTerritory --copy`), correct the summary (still "will add"), publish page 54 (it was `not_published` at the close). Per `Stable/README.md` a drained plan makes the mod a `Stable/` candidate (`Development_Tools/Move-ModFolder.ps1`), not moved here.
- **Verification debt:** the 16 person rows above.

### 2026-10-18 - second MASTER_PLAN closed (vanilla balance + niche content)
- **Verdict:** section 1 (balance pass FT-B1 to FT-B25) built in 0.1.7; section 2 N1-N4 in 0.1.8, N6 (Island Start) in 0.1.9, N5 (the island outrigger as a researched build) and the cooked-food art in 0.1.10. 0 open prompts.
- **Disposition:** CLEARED. Both plan files removed; the as-built record is `Documentation/Design/FarTerritory_As_Built.md` section 23 (with its Considered, not selected table), and the 23 tracker rows that cited the plan now cite that section.
- **Evidence:** CSFF `8dd1c9324` (N5, 0.1.10) and this commit (art); FT gates 76/76, card-art gates 80/80, harness dry run 6/6, build 0 warnings.
- **Verification debt:** T2.1774-T2.1797 and T2.1911, all awaiting a person at a running game (T2.1797 is the owner's art review).
