# Roadmap: Far Territory
Version at time of writing: 0.1.16
Date: 2026-11-15
Audit score: 9/10 - SHIPS WITH CAVEATS (0 CRITICAL, 0 design gaps, 3 open WARNING [M3 grassland Meadowgrass dup; BP1/BP2 cosmetic Cinchona Decoction blueprint nits], 0 open MINOR. See `.audit/summary.md`)

## Current State

**Theme**: Lands beyond the forest, reached by ordinary travel - a tropical island (a tribute to Card Survival: Tropical Island, now drawing CSTI's own art), plus a mainland grassland, snowy mountains and cold fjords. Fishing, foraging, climbing, butchery, sea dangers and a castaway start, for a mid-to-late survival loop.

**Content**: 99 items / 5 blueprints / 167 location + 50 environment cards / 2 liquids / 1 perk (Island Start) / 9 animals / 9 GIF cards / 184 custom images.

**Stability**: 9/10 - SHIPS WITH CAVEATS. 0 CRITICAL, 0 design gaps, **3 open WARNING (M3 + the two cosmetic Cinchona blueprint nits BP1/BP2)**, 0 open MINOR. The v0.1.16 cut (`d947c1218`) closed the Cinchona Decoction unbuildable-recipe bug (`87a6e84ff`): the brew now takes plain `LQ_Water` with a Fuel range heated on a Campfire, not the unreachable `OLD_LQ_StewWater` no vessel can hold (re-verified clean by the medplants/modwide/palms module re-audits and the fresh blueprints re-run `64619cd05`). The mechanical open warning is **M3** (grassland, `74ef899fd`) - Wildrye Plain's `DefaultEnvCardDrops` lists the vanilla Meadowgrass Patch twice, but that card is `UniqueOnBoard: true`, so the board holds one; README `:340` ("Two Meadowgrass Patches") is a verified false claim and the second entry is dead data. **BP1/BP2** are two cosmetic blueprint-completeness nits on `Bp_FT_CinchonaDecoction` from the 2026-11-15 blueprints full re-run (`64619cd05`): no `WhenCreatedSoundsWarpData` creation sound (BP1) and no empty `BuildingStatConditions` field (BP2), unlike the four sibling blueprints; neither affects gameplay (the recipe is buildable), and both are small author content fixes, not applied this .audit-only pass. The three items carried into 2026-11-14 are now closed: **W4** (palms Light>=10 + Stamina>=11 gate on Husk/Crack/Fell/Cut) - the gate is vanilla's own (310 of 988 CT0 in-hand CardInteractions carry the same Light `[10,100]` entry; every open-air place adds passive Light +10 that meets it at night), it is now documented on each action in README `:202-204` + a rule line, and the palms module re-run 2026-11-15 (`28a745f1c`) is SOLID, gate `FarTerritory-PalmHandworkGates.Tests.ps1` 12/12; **M3-doc** (As-Built `:382` fjord musk-ox "15%" vs shipped 6%) - fixed 2026-11-14 (`c78e39415`); **N1** (cave ambient-sound falloff) - refuted 2026-11-14 (every framework-built FT animal has an empty `AmbientSounds` list, nothing to fall off). Everything else is resolved and verified: W1 (framework floor) + W2 (dead-code modder docs) shipped 0.1.15; W3 (night Forage) fixed 2026-11-12 (`2e9b8a9c3`); the Harsh Wilds carcass drop-chance *player-facing* README docs fixed in `68cfe1093`; the cosmetic unused-`Harmony` parameter dropped 2026-11-12. The 2026-11-04 garden/cave CRITICALs remain refuted false positives, gated by FT-B1b / FT-B13.

**Open work**: No open or pending retrospective names Far Territory, `crispywhips.far_territory` or FT. Three open WARNINGs (M3 + BP1/BP2), filed in `.audit/summary.md` and recorded, not fixed, this consolidation pass (the run edited nothing under `FarTerritory/` outside `.audit/`+`ROADMAP.md`; M3 needs an owner decision, BP1/BP2 are small author content fixes). The W4 palm/coconut gate, the M3-doc As-Built drift and the N1 cave-ambient cosmetic that were open on 2026-11-14 are all closed (documented / fixed / refuted; see Phase 0). No plan file is open (`Documentation/Plans/FarTerritory/` does not exist); the CSTI art adoption plan built in v0.1.13 and the balance/niche plans closed into `Documentation/Design/FarTerritory_As_Built.md` sections 23/24.

**Framework compliance**: Tier 2. C# is **three** small classes (`Patcher/ClimateService.cs`, `Patcher/LandmarkSeed.cs`, `Patcher/OwlIslandGuard.cs`) plus `Plugin`, using `ContentModPlugin`, `SpawnService`, `TickEvents` and the framework's `Api.Npc.RegisterMoveGuard` registry; FT patches Harmony nowhere directly (the one `MoveNPC` prefix lives in the framework's `Patching/MoveGuardPatch.cs`). Board pre-create is the declarative `EnvPreCreate.json`, read by the framework's `EnvPreCreateService` (replaced the deleted `Patcher/EnvSaveDataPreCreate.cs`, `8e4d185cd`). **Framework floor is 2.29.1** (in `ModInfo.json`, `README.md`) - the release that first publishes `EnvPreCreate.json` support (tagged 2026-11-08) and `Api.Npc.RegisterMoveGuard`; the framework is on Nexus as 2.29.2, uploaded before FT 0.1.15, so players are covered. The 0.1.16 Cinchona fix is pure JSON/CSV and added no new framework API use. No deprecated patterns (no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no manual perk/blueprint injection, no `ModLoaderVerison`).

---

## Phase 0: Stabilize

> Three open warnings: M3 (owner design call) and the two cosmetic Cinchona blueprint-completeness nits BP1/BP2 (a small author content pass). Nothing is a crash/corruption blocker; the one shipped-but-unbuildable recipe (Cinchona) is fixed in 0.1.16 and re-confirmed buildable by the 2026-11-15 blueprints re-run, and the W4 / M3-doc / N1 items open on 2026-11-14 are now closed. The resolved rows are held as the record.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **M3 (OPEN): "Two Meadowgrass Patches" is one** - `FT_WildryePlain.json` lists the `UniqueOnBoard` Meadowgrass Patch twice (one spawns); README `:340` overstates. **Owner design call:** (a) remove the duplicate entry + README `:340` -> "A Meadowgrass Patch"; or (b) swap the duplicate for a *different* non-unique forageable to actually give two clumps. When fixed, re-run the grassland mod-report dimension (its "intentional" line is stale). Both files under `FarTerritory/`, not touched this .audit-only pass. | Mechanic vs docs | P0 (design decision) | Quick |
| **BP1/BP2 (OPEN): Cinchona Decoction blueprint-completeness nits** - `Bp_FT_CinchonaDecoction.json` has no `WhenCreatedSoundsWarpData`/`WarpType` creation sound (BP1, add `["Flowing1"]` + WarpType 3) and no empty `BuildingStatConditions` field (BP2, add `[]`), unlike its four sibling blueprints. Cosmetic/completeness only; the recipe is buildable. Surfaced by the 2026-11-15 blueprints re-run (`64619cd05`); small author content pass, not promoted to a plan (Warnings are polish), not applied this .audit-only pass. | Cosmetic (blueprint) | P2 (polish) | Quick |
| ~~W4: undocumented night/dark-cave coconut gate~~ **RESOLVED 2026-11-14** (`c78e39415`) - the Light>=10 + Stamina>=11 gate on Husk/Crack/Fell/Cut is vanilla's own (310 of 988 CT0 in-hand CIs carry it; open-air passive Light +10 meets it at night) and is now documented on each action in README `:202-204` + a rule line; palms module re-run SOLID 2026-11-15 (`28a745f1c`), gate `FarTerritory-PalmHandworkGates.Tests.ps1` 12/12. The recommended Light-entry removal was NOT made (it would make these four unlike vanilla handwork). Palms local id M3/M4. | Mechanic vs docs | done | - |
| ~~M3-doc: As-Built `:382` fjord musk-ox "15%"~~ **RESOLVED 2026-11-14** (`c78e39415`) - `:382` now reads "(6%, ... tuned down from 15% in FT-B2, section at line 767)", matching shipped `Chance: 6.0`. Claude-facing doc. | Docs honesty | done | - |
| ~~N1: cave ambient-sound-falloff cosmetic~~ **REFUTED 2026-11-14** (`c78e39415`) - every framework-built FT animal has an empty `AmbientSounds` list (`AnimalAssetFactory.cs:149`), so the Macaque/Monitor/Bat have no ambient sound to fall off in a cave. No change to the mod. | Cosmetic | done | - |
| ~~Cinchona Decoction unbuildable recipe~~ **RESOLVED v0.1.16** (`87a6e84ff`) - now requires plain `LQ_Water` + Fuel range (campfire-heatable), not the unreachable `OLD_LQ_StewWater`; gate `Blueprint-RequiredLiquidObtainable.Tests.ps1`; re-verified by medplants/modwide/palms | Mechanic (unbuildable) | done | - |
| ~~W1 (framework requirement)~~ **RESOLVED 0.1.15** - floor raised 2.28.3 -> 2.29.1; the two needed APIs shipped in framework 2.29.1 (Nexus 2.29.2, uploaded before FT 0.1.15) | Feature honesty / release | done | - |
| ~~W2 (dead-code modder docs)~~ **RESOLVED 0.1.15** - README "For modders" reworded to three real classes, no Harmony patch, owl guard via `Api.Npc.RegisterMoveGuard`, `EnvPreCreate.json` line added | Docs honesty | done | - |
| ~~W3 (outdoor Forage offered at night)~~ **RESOLVED 2026-11-12** (`2e9b8a9c3`) - Forage `TriggerRange.x` 10 -> 15 on the outdoor-with-passive-Light+10 subset + 4 node actions; dark caves (no passive) left at 10; gate `FarTerritory-OutdoorForageNeedsDaylight.Tests.ps1` 15/15 | Mechanic vs docs | done | - |
| ~~Harsh Wilds carcass drop-chance *player-facing* README mismatch~~ **RESOLVED** (`68cfe1093`) - README 347/402 and As-Built section 15 aligned to the tuned data (wisent 4%, musk ox 6%). As-Built section 14 (the M3-doc miss) fixed 2026-11-14 (`c78e39415`) | Docs honesty | done | - |
| ~~Unused `Harmony` parameter on `OwlIslandGuard.Initialize`~~ **RESOLVED 2026-11-12** - method group wiring in `Plugin.cs`; modwide/code-quality 10/10 | Cosmetic | done | - |

---

## Phase 1: Foundation

> Table-stakes health, all currently met - held here as the maintenance baseline.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Versions in sync across ModInfo / Plugin.cs / README (all 0.1.16) | Version hygiene | - | met |
| Chinese localization parity (SimpCn CLEAN, 1958/1958, 0 missing/extra/no-CJK/stale) | Localization | - | met |
| Framework Tier 2 (ContentModPlugin / SpawnService / TickEvents / Api.Npc) on a released framework floor (2.29.1) | Framework | - | met |
| bin/Release in sync with source (BinSync 0/0/0) | Build hygiene | - | met |
| Garden-crop and cave mechanics pinned by FT-B1b / FT-B13 gates | Regression guard | - | met |
| Cinchona recipe input reachability pinned by `Blueprint-RequiredLiquidObtainable.Tests.ps1` | Regression guard | - | met (new 0.1.16) |

---

## Phase 2: Core Expansion

> No plan is open. The items below are README-acknowledged gaps, not scheduled work; the mod is pre-release and its author has flagged its numbers as first guesses. (M3 option (b) - a second distinct grassland forageable - is small content that would also close M3 while adding depth.)

### Cooked shellfish / seaweed (README gap, unplanned)
**What**: A cook path (steam or boil) for Conch, Oyster, Sea Urchin and Seaweed, mirroring the gutted-fish cook chain already shipped.
**Why**: README states "Shellfish and Seaweed have no cooked form yet" - an incomplete loop in the fishing/foraging food chain.
**Requires**: none.
**Complexity**: Medium.

### Coconut Milk / richer stew (README gap, unplanned)
**What**: A Coconut Milk liquid (grate + press meat) feeding a richer stew variant.
**Why**: README notes its deliberate absence. Only worth building if it earns its weight in the food chain - revisit against play data.
**Requires**: none.
**Complexity**: Medium.

---

## Phase 3: Integration & Depth

> The four soft crossovers already ship; these are the live integration points to keep healthy as the partner mods move.

### Spice flavour synergy (README gap)
**What**: Confirm (in game or via a `FlavourMatrix/*.json` row) that Ginger, Chilli, Lemongrass and Jasmine actually flavour base-game dishes as the README implies.
**Why**: README says "Whether a base-game dish picks these up has not been checked in game" - an advertised interaction with no verification. Note the FlavourTags Intensity enum rules in CLAUDE.md (0 = Medium, 1 = Strong, 2 = Subtle) before authoring any intensity, and that an eat/drink action needs `ReceivingCardFlavourBonus: true` to taste its card.
**Requires**: a FlavourTag authoring pass or a tracker row.
**Complexity**: Medium.

### Keep crossovers current
**What**: Re-verify the Harsh Wilds carcass hand-off (data 4%/6%, player-facing README now matches; As-Built `:382` is M3-doc), Forest Lore Standing Stones seed, Sirus23 owl guard (`OwlIslandGuard`, on `Api.Npc.RegisterMoveGuard`) and ACT Grappling Hook / Climbing Aid stack whenever those partner mods release.
**Why**: cross-mod UID references break silently when a partner renames content.
**Requires**: coordination with each partner mod's version.
**Complexity**: Quick per check.

---

## Phase 4: Polish

> Art is complete (CSTI adoption shipped 0.1.13); the remaining polish is tuning once play data exists.

| Item | What | Complexity |
|------|------|------------|
| W4-sweep (W4 itself done) | W4's palm/coconut Light gate is now documented (README `:202-204` + rule line, 2026-11-14) and the gate is vanilla's own. Remaining optional follow-up: sweep medplants/foodplants/fishing in-hand processing CIs for the same copy-pasted Light entry and reconcile each against its own README line (palms CA out-of-module note) | Quick |
| Cave/shore forage tuning | README: "Every cave's forage is a first draft" - a balance pass once play data exists; pin any tuned number in the FT-B* gates | Medium |
| Cooked-food / fishing number tuning | README calls these "first guesses" - rebalance against real runs | Medium |
| Close the person-only tracker rows | Net/funnel-trap feel (T2.1393), the owl through a real night (T2.1524 human half), existing-save animals/path cards | Quick per row (needs a human at a running build) |
| ~~N1 cosmetic (caves)~~ | REFUTED 2026-11-14: every framework-built FT animal has an empty `AmbientSounds` list, so there is no ambient sound to fall off in a cave. No work remains | done |

---

## Long-term Vision

> Where Far Territory should be at v1.0.

A complete, CSTI-faithful island tribute with its own art (shipped), sitting beside a fleshed-out mainland (grassland, mountains, fjords), where every advertised loop - fishing, foraging, climbing, butchery, cooking, the castaway start and the crossovers - is live-verified and tuned against real play, shipping cleanly on a released framework floor (met at 2.29.1). With the art plan done, the Cinchona recipe now buildable, and every crash/corruption-class warning closed (plus the W4 coconut-gate documentation, the As-Built fjord doc drift and the N1 cave cosmetic all closed 2026-11-14/15), the remaining open work is M3 (an owner design call on the grassland forageable count), tuning passes and closing the person-only tracker rows, not new systems.

**Potential major additions** (not yet justified - revisit after a tuning pass):
- Coconut Milk liquid feeding a richer stew variant - README notes its absence; only worth it if it earns its weight in the food chain.
- Deeper mainland content (more fjord/mountain fauna and structures) - the mainland is thinner than the island; a natural expansion now that the island is art-complete.
- A salvage action and/or bespoke art for the Wrecked Ship (currently decoration-only, reusing the Driftwood sprite) - island CA flagged this as a judged trade-off.

Any of these worth building belongs in a plan under `Documentation/Plans/FarTerritory/` (or its Considered table, with the reason); never a stub under `Documentation/Ideas/`, which keeps no backlog since 2026-10-01.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod FarTerritory` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After fixing a finding | Run `/critical-analysis FarTerritory` to verify the fix |
| After a content phase complete | Run `/export-to-repo FarTerritory` / `/nexus-publish` and bump the version |
| Stale top-level trio / per-category reports | `/critical-analysis FarTerritory` + `/audit-mod FarTerritory` to re-seat the 2026-10-20/21 reports (they describe a C# surface, a framework requirement, the pre-fix decoction recipe and numbers the refactors + release-prep + balance passes changed; the module wave already supersedes them) |

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
/nexus-publish upload FarTerritory - publish a new file version to Nexus
```

---

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

### 2026-10-28 - MASTER_PLAN (CSTI art adoption), /audit-plan
- **Verdict:** NOT drained. 0 of the plan's art phases built: W0.1 (sky key), Phases A1, A2, B, C and D and the shipping obligations are all OPEN; only the 2026-10-20 tooling and index exist (re-verified, collapsed to one line). Nothing awaiting verification, nothing FAILED, nothing OBSOLETE.
- **Disposition:** KEEP IN PLACE - code and art work remain. Next: Phase A1 (keyed items, no dependency) with the W0.1 trace beside it. No pack yet. Mission `csff-plans-prune-2026-10-28`.
- **Pruned:** the plan's "Built so far" bullets (one line now; full text at `02068ad6e`). A dated status paragraph appended to the plan.
- **Evidence:** at `efe084587`: `git log 02068ad6e..HEAD -- Development_Tools/CardArt/csti_art.py Documentation/Research/FarTerritory/csti` returned no commits; a case-insensitive grep for `csti` over `FarTerritory/.art/` (193 records) returned 0 files while the same four `.art` records read back nano-banana-2; `csti_art.py` subcommands are `index`, `match`, `export` with no `--sky`; `wc -l` over the five TSVs gives 1140, 2375, 1874, 323 and 38 lines (header included), matching the plan's counts; `FT_Seashells` is named by 4 card JSONs.
- **Verification debt:** none added (no art shipped).


### 2026-11-06 - Fleet_APE_Port
Scope: Fleet
- **Verdict:** Fully landed. Section 10 (W0.10) rows 11 and 14 - the plan's last two open code
  rows - built, build-verified across every affected mod, and gate-verified in Prompts 45 and
  46 (`Api.Npc.RegisterMoveGuard`/`MoveGuardPatch` for row 11; `GameQuery.IsInPlayerEnv` made
  public plus 3 call-site migrations for row 14). Every other section (1, 2, 3, 4.1, 5.1, 5.7,
  6.4, 6.5, 8, 9) was already reduced to as-built lines in earlier prompts. 0 open code rows
  remain in the plan or its pack.
- **Disposition:** ARCHIVE - plan doc moved to `Documentation/Design/Fleet_APE_Port_As_Built.md`
  (full history preserved, nothing summarized away). Pack
  (`MASTER_PLAN_Implementation_Prompts.md`) DELETED per policy (packs are always deleted, never
  archived); its citations elsewhere were repointed to the landing commit of the row each one
  names, since no archive exists for it.
- **Pruned:** `Documentation/Plans/Fleet/MASTER_PLAN.md` and
  `Documentation/Plans/Fleet/MASTER_PLAN_Implementation_Prompts.md`, both now removed from
  `Documentation/Plans/Fleet/`.
- **Evidence:** Row 11 commit `9a5305424` (Prompt 45, `Api.Npc.RegisterMoveGuard` +
  `MoveGuardPatch`, collapsed into the pack by `a3c7ab227`); row 14 commit `5c28c03e4` (Prompt
  46, `GameQuery.IsInPlayerEnv` public plus the 3 call-site migrations in one commit, collapsed
  into the pack by `e01e911ef`); unrelated pre-existing gate fix found during verification,
  commit `6f0a0bc91` (`CMC-SilentExits.Tests.ps1`, 3 stale rows from the earlier StatAccess
  migration); citation sweep commits `6567bc510` (`.claude/playthrough-test-status.json`
  private-index fix) and `e01776032` (gate comment + `AUTHORING.md`). `python3 -c` count against
  `.claude/playthrough-test-status.json`'s `items` bucket for rows citing
  `Fleet_APE_Port_As_Built.md`: 4 pending, 0 dropped; `confirmedItems` bucket: 25 already
  confirmed. `git status --porcelain -- Documentation/Plans/Fleet/` empty after the move.
- **Verification debt:** 4 rows still `pending` in `.claude/playthrough-test-status.json`
  `items` as of this sweep: `cmc_homestead_kit_metal_nuggets`,
  `fw_mod_action_sounds_resolve_smithing_heard`, `fw_heated_metal_art_look_person`,
  `act_metal_gloves_art_look_person`. Per CLAUDE.md "code work never waits on a human
  playthrough," these do not block this archival; they remain open tracker rows for a future
  human playthrough pass to confirm.
