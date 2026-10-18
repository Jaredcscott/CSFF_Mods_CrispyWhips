# HomesteadPerks Roadmap

No phased roadmap has been generated for this mod yet (`/roadmap HomesteadPerks` writes one). This
file exists so the plan-reconciliation record below has its durable home; a future `/roadmap` run
keeps that section verbatim as its last H2.

## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b (here, by the session that drained the plan). Append-only:
> never reword, reorder or delete an entry. `/roadmap` Step 7 preserves this section verbatim when
> it overwrites the rest of this file.

### 2026-09-28 - Content_Update_Plan
- **Verdict:** 3 of 3 selected features BUILT as HomesteadPerks 1.3.0 (Rustic Table Kit, Bookshelf
  Kit, Sledge), 0 code rows left open. No prompt pack ever existed for this plan. Its "Considered, not
  selected" table (Table/Chair `NOT_USED_` placeholders, a standalone Stool perk, Chest, Travois,
  the Abandoned Homestead ruin, CMC's carpentry furniture, the DecorationAndComfort stubs, WDI's
  starting perks, the CMC overlap and the concept-mod stubs) carries every rejection reason with it
  into the archived doc; Travois is the one row left open as a future candidate, not ruled out.
- **Disposition:** DELETE from `Documentation/Plans/HomesteadPerks/` and ARCHIVE to
  `Documentation/Design/HomesteadPerks_Content_Update_1_3_0_As_Built.md` (plan verbatim, plus an "As
  built" section that wins where the build departed). The `Documentation/Plans/HomesteadPerks/` scope
  folder is gone.
- **Pruned:** the whole plan doc (285 lines), archived rather than dropped. The build departed from
  it in three ways, all recorded in the archive: the perks grant vanilla's own carried furniture
  (`RusticTableDismantled`, `Stool`, `BookShelfDismantled`) instead of three new HSP kit cards (owner
  decision 2026-09-28); the plan's `"Stool"`/`"Bookshelf"` sprite names do not exist, so the icons are
  `Table`, `Shelf` and `Sledge`; and the stat the furniture raises is Sanctuary, not "Comfort".
- **Evidence:** vanilla carried forms read from the EA 0.68b export
  (`Documentation/GameData/CSFF-JsonData_Current/.../CardData/`); sprite names measured in the game's
  `sharedassets0.assets` with UnityPy against positive controls (`RusticTable` resolved to Sprite
  `Table`, `Sledge` to `Sledge`, matching the export) - `Stool`, `StoolPlaced`, `Bookshelf` and
  `BookShelfDismantled` reference no Sprite in any asset file. Every granted GUID resolves in
  `UniqueIDScriptableGUID/CardData.json` to the intended card; DisplayPriority 2830/2840/2850 unique
  across the mod. Sledge spawn state from `.decomp/GameManager.cs` `GetStartingCardsFromArray` (no
  transferred durabilities) and `.decomp/InGameCardBase.cs` line 5695 `GenerateStartingValue()`:
  2688 of 2688, `RandomStartingOffset` 0. Pester: `PerkUid-AliasAndNameCollision`,
  `CardData-Integrity`, `Localization-DuplicateKeys`, `Localization-EnglishColumnCjk`,
  `Preflight-LocalizationParity`, `Csproj-ContentIncludes` 107/0. CSV appends +6/-0 per language.
  **What no check here can see:** that the perks deliver and place correctly in a running game. That
  is T1.127 (`pending`, AUTO harness scenario
  `TestHarness/client/scenarios/HSP/T1.127_hsp_1_3_0_table_bookshelf_sledge_perks.json`, dry-run
  valid), filed in `.claude/playthrough-test-status.json` BEFORE this record was written.

### 2026-10-03 - MASTER_PLAN (HomesteadPerks round 2), `/audit-plan` pass
- **Verdict:** audit only, plan KEPT. 0 of 16 features built (1-13, 15-17), 0 pruned, 0 prompts
  pruned (20 open, 0 awaiting verification in the pack); 5 research items (R1-R5) all still unrun
  except R4's offline half, now measured. The mod is still 1.3.1, 16 perks, 13 kit cards; there is no
  HSP scenario beyond T1.127's, no weight-line gate and no HSP row in the reserved Track 1 block.
- **Disposition:** KEEP in `Documentation/Plans/HomesteadPerks/` (not drained, mod stays in the
  repo root, not a `Stable/` candidate).
- **Corrected (STALE-CLAIM, noted in the plan's status block):** (1) the plan said Advanced Copper
  Tools' `advanced_copper_tools_copper_nails` has only `FuelCapacity` active; it also has
  `SpecialDurability4` (metal type) at 100 = Copper, which is the correct default, so the feature 13
  build is unchanged. (2) R4's offline half: `Bp_RusticTable` and `Bp_Sledge` ARE in journal tabs;
  `Bp_Bookshelf`, `Bp_Chest` and `Bp_RusticStool` are in none, so feature 15 only needs the live read for
  those three. The pack's Prompts 1, 16 and 17 and its wave line were updated to match.
- **Evidence (ground-truthed this pass):** every cited vanilla GUID, CardType, `ObjectWeight`,
  research-tick and stage count, `VanillaSprites.txt` name (Kiln has none, as the plan says) and
  `CardTabGroup` membership read from the EA 0.68b export; HSP's 16 `SunsCost`, `DifficultyRating`
  and `DisplayPriority` values and the 13 kit weights read from the files; csproj Content includes
  (lines 94-97) and `Pack-Suite.ps1` membership; `ModRegistry.IsLoaded` has no caller; no Place DA
  carries a condition; only the Rustic Table and Bookshelf descriptions carry a weight sentence;
  `.claude/nexus-publish-status.json` holds 1.2.4; tracker rows T1.127 (`pending`), T1.53/T1.54
  (`confirmed`), T3.42 and T4.41 (`pending`). **Not re-read:** the plan's `.decomp/` line citations
  (the directory is absent in this checkout), anything that needs a running game (R1-R3, R4's live
  half), and the 2026-09-29 `Sirus23_Mod_Collection` audit text behind feature 5's open fact.
- **Docs drift seen, not edited here:** `HomesteadPerks/.audit/summary.md` still reads 1.3.2 in its
  Versions row (the dispatcher folded 1.3.2 back into 1.3.1 on 2026-09-29; `.claude/mod-audit-status.json`
  says so, ModInfo/Plugin/README/CHANGELOG agree on 1.3.1); `HomesteadPerks/Features.json` is 1.2.3
  and omits the 1.3.0 perks (known, no gate reads it). README line 46 and CHANGELOG 1.3.0 say the
  Bookshelf and Rustic Stool "skip vanilla's research and building" although `Bp_Bookshelf` and
  `Bp_RusticStool` are in no journal tab: plan feature 15 owns that wording.

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 16 pack prompts, 16 plan features (1-13 and 15-17) and research items R1-R5 checked. Verified done 3 (features 8 and 9 with T1.131 and T1.132 pass, R5 with `59a0df5fd`); built-awaiting 2 (feature 7 Travois T1.130, feature 13 Copper Nails T1.134); open 13 features and 4 live research items (R1 to R4); 16 open prompts before and after. No STALE-CLAIM and no FAILED row found.
- **Disposition:** KEEP IN PLACE (open work remains; the mod is not a `Stable/` candidate).
- **Pruned:** nothing this pass; the plan and pack were already pruned by the refresh earlier on 2026-10-04 (features 7 to 9 and 13 collapsed, pack renumbered to Prompts 1-16). Only a dated paragraph was appended to the plan's Status block. Sections 8 and 9 were kept as one-line DONE stubs because tracker rows T1.131 and T1.132 cite them by section number.
- **Evidence:** `HomesteadPerks/CharacterPerk` holds 19 perk files and `CardData/Item` 15 kit cards (ls); the highest `DisplayPriority` is 2930 and Travois 2910, Kiln 2920 and Drying Rack 2930 match the plan's table (2860 to 2900 unused); `ModInfo.json` Version 1.3.1 and the deployed `Homestead_Perks/ModInfo.json` Version 1.3.1; grep for `RequiredCardsOnBoard|RequiredTagsOnBoard` in `HomesteadPerks/CardData` = 0 files (control: the same grep over `WaterDrivenInfrastructure/CardData/Item` lists `FishFunnel_Kit.json` and `Fishpond_Kit.json`, and `MillRaceOutlet_Kit.json` carries 2 `Inverted`), so features 1, 2, 4 and 12 are unbuilt; `Development_Tools/Tests` has 0 files matching `WeightLines` (control: 1 file matches `PerkUid`), so feature 3's gate is unbuilt; `Documentation/Research/HomesteadPerks` holds only `wave0-r5-parked-stations.md`, so R1 to R4 are unrun; `HomesteadKitPatch.cs` line 98 calls `ModRegistry.IsLoaded(CopperNailsModName)` (feature 13 built); `TestHarness/client/scenarios/HSP` holds T1.127 and T1.130 to T1.133 and the T1.130 scenario asserts no 'no other perk card' clause (the pack's open clause on T1.130 is true); `.claude/game-queue.txt` line 13 still queues Prompt 1; every T-id left in the plan and pack (T1.53, T1.54, T1.127, T1.130 to T1.134, T3.42, T4.41) exists in `.claude/playthrough-test-status.json` (script, 10 of 10 found). **Not re-read:** `.decomp/` line citations (folder absent in this checkout), anything needing a running game (R1 to R4), and the Sirus23 audit text behind feature 5's open fact.
- **Verification debt:** T1.130 (pending; plus the unasserted 'no other perk card' clause) and T1.134 (pending). Both have tracker rows. T3.42 (ACT) and T4.41 (H&F) are other mods' pending rows the plan only cites.

### 2026-10-05 - MASTER_PLAN section 13 (Copper Nails with Advanced Copper Tools)
- **Verdict:** feature 13 DONE. T1.133 (ACT loaded: 50 nails beside 20 Metal Nuggets) passed 2026-10-04; T1.134 (ACT set aside: 20 nuggets, no nails, handler ran once, no error) recorded as a harness PASS 2026-10-05 in `76830924a` from report `t1-134-...-20261005-035020`, after a lane-runner read and an adversarial challenge that disputed nothing.
- **Disposition:** section 13 reduced to a DONE stub (tracker rows cite it by number); its Awaiting-verification entry removed from the pack. The plan stays: features 1-6, 10-12 and 15-17 are unbuilt and feature 7 awaits T1.130.
- **Evidence:** `.claude/playthrough-test-status.json` rows `hsp_homestead_copper_nails_with_act` (T1.133, `status` pass, `confirmedOn` 2026-10-04T03:30:00Z) and `hsp_homestead_no_nails_without_act` (T1.134, `confirmedOn` 2026-10-05T21:15:30Z, recorded by commit `76830924a`); line added 2026-10-05 from those rows because the entry as first written carried its evidence only inside the Verdict line.
- **Caveat carried forward:** T1.134's steps 18-20 cannot see a handler throw (ActionRouter logs it at Warn under the handler name `HomesteadKitPlace`), so its no-error clause rests on step 16; copy that pattern with care.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** 16 plan features (1-13, 15-17) and Wave 0 R1-R5 checked against HSP source at `56ef680ed`, the tracker and the harness reports. VERIFIED-DONE, deleted: features 8, 9, 11 and 13 (T1.131, T1.132, T1.136, T1.133, T1.134 all in `confirmedItems`), R5, and feature 3's weight-line half (`9b3dab9c0`). BUILT-AWAITING (collapsed to pointers): features 5 and 6 (T1.138), feature 7 (T1.137). OBSOLETE: the Spinning Wheel "does not resolve" branch (T1.135 step 12). STALE-CLAIM corrected: 2 (section 0's perk count 19 -> 25; pack Prompt 3's "convert the Bloomery Kit to a kit-card carrier", when it already grants `hspbloomerykit`). FAILED: 0. OPEN: features 1, 2, 3 (remainder), 4, 10, 12, 15, 16, 17, plus R1-R4. Pack 13 open before and after; 2 awaiting before and after (Bloomery left the list, T1.137/T1.138 replaced the confirmed T1.130/T1.135 halves). First entry Prompt 1 (deps: none) is the true next action.
- **Disposition:** KEEP IN PLACE - 13 open prompts of code and research work remain (features 1-4, 10, 12, 15-17 all wait on the live-game reads R1-R4 in Prompts 1 and 2).
- **Pruned:** plan sections 8, 9, 11, 13 and their Selected rows; the R5 row of the Wave 0 table; the open question "Bloomery Kit shipping at all"; the Spinning Wheel risk row; section 3's done paragraph (one sentence left); sections 5, 6, 7 cut to one-line pointers; the status history condensed (full text resolves at `56ef680ed`). Pack: the Bloomery and Spinning Wheel lines of the Awaiting list, Prompt 1 item 5's Spinning Wheel resolve check; the Awaiting list moved under a real `## Awaiting verification` heading. Section 11's kit-card rule list was folded into section 10 before deletion (sections 10 and 17 and pack Prompt 14 cite it there).
- **Evidence:** `ls HomesteadPerks/CharacterPerk` = 25 files, `CardData/Item` = 16 (no Enclosure, Chest or mine kit files); grep `193f3be3|RequiredTagsOnBoard|tag_EnvIndoors` over `CardData/Item` hits only `HSP_LogBedKit.json`, and that hit is its own `TransformIntoWarpData` (BedLog), so 0 guards (`RequiredCardsOnBoard` count 0 in `HSP_CellarKit.json` and `HSP_PathKit.json`); `Documentation/Research/HomesteadPerks/` holds only `wave0-r5-parked-stations.md` (R1-R4 unrun); `HomesteadPerks-WeightLines.Tests.ps1` run offline 4 passed 0 failed; `HSP_BloomeryKit.json` `ObjectWeight` 900 and `Pk_BloomeryKit.json` grants `hspbloomerykit`; report `t1-135-...-20261006-184926` step 12 `uid:8c911203...` count ==1 PASS (control step 3 ==0 on a no-perk start); `T1.130_hsp_travois_perk.json` has 8 steps, none on other perks' cards (T1.137 still true); tracker: T1.137 and T1.138 in `items` pending, T1.130-T1.136 in `confirmedItems`; deployed `Homestead_Perks` MD5 equals source for both CSVs, `Pk_CabinKit.json` and the DLL (Prompt 2's deploy dependency MET). Not re-read: `.decomp/` citations (folder absent) and anything needing a running game.
- **Verification debt:** T1.137 (Travois: no other perk's card in the carried row, person), T1.138 (place each workstation once, person). T2.433 (CMC's row on the Homestead Kit material stats) has a PASS report on disk from 2026-10-07 06:11 not yet recorded; it is CMC's row, not this plan's.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** counts: 9 open plan features (1, 2, 3 remainder, 4, 10, 12, 15, 16, 17) plus Wave 0 R1-R4, 3 built-awaiting sections (5, 6, 7) and 15 pack entries (13 OPEN prompts, 2 Awaiting bullets) re-checked against HSP source at `8ff701049`, the tracker and the harness reports. VERIFIED-DONE 0, OBSOLETE 0, FAILED 0. BUILT-AWAITING 3 sections behind 2 rows (T1.137, T1.138), both with a harness PASS on 2026-10-09 not yet recorded. STALE-CLAIM corrected 2 in the pack: the Awaiting bullets and Prompt 16 item 4 called T1.137 and T1.138 person rows, but `9191af9f9` moved both to the harness lane. OPEN 13 prompts before and after. First entry Prompt 1 (deps: none) is the true next action, then Prompt 2 (live game).
- **Disposition:** KEEP IN PLACE - 13 open prompts of research and build work remain, all behind the two live-game research prompts; the mod is not a `Stable/` candidate.
- **Pruned:** nothing. Plan: one dated paragraph appended to the Status block. Pack: header and Next line rewritten (the next wave named), the two Awaiting bullets rewritten to cite the 2026-10-09 PASS reports, Prompt 2's deploy dependency re-dated, Prompt 16 item 4 reworded.
- **Evidence:** `git log 56ef680ed..HEAD` over HomesteadPerks source (CharacterPerk, CardData, Localization, Patcher, README, CHANGELOG, ModInfo.json and the WeightLines gate) = 0 commits (control: the same range over `HomesteadPerks` plus the HSP scenarios returns 4, `bin/Release` and the T1.137/T1.138 scenarios only); `ls HomesteadPerks/CharacterPerk` = 25, `CardData/Item` = 16; grep `RequiredCardsOnBoard|RequiredTagsOnBoard` over `HomesteadPerks/CardData/Item` = 0 files (control: `WaterDrivenInfrastructure/CardData/Item/MillRaceOutlet_Kit.json` carries 2 `Inverted`); `Documentation/Research/HomesteadPerks/` holds only `wave0-r5-parked-stations.md`; tracker: T1.137 and T1.138 in `items` pending, T1.130-T1.136 in `confirmedItems`, both pending ids listed in `TestHarness/client/machine_rows.json`; reports `t1-137-...-20261009-073425` (276 of 276 steps PASS) and `t1-138-...-20261009-071808` (75 of 75 PASS, seven placements); deployed `Homestead_Perks` MD5 equals source for both CSVs, `Pk_CabinKit.json` and the DLL (equals `bin/Release`), Version 1.3.1 both; `.claude/nexus-publish-status.json` HomesteadPerks 1.2.4, framework 2.26.12; `.claude/game-queue.txt` still queues Prompt 1 (csff-mods-ff); no worktree besides master. Not re-read: `.decomp/` citations (folder absent) and anything needing a running game.
- **Verification debt:** T1.137 (Travois: no other perk's card) and T1.138 (each workstation placed once), both harness PASS 2026-10-09 awaiting record after skeptic review.

### 2026-10-09 (later) - MASTER_PLAN, second /cleanup-plans pass
- **Verdict:** NOT drained. 13 open prompts (1-6, 9, 11-16), 2 awaiting verification (T1.137, T1.138). 0 pruned, 0 obsolete, 0 stale claims. Every offline prompt waits on the Wave 0 live reads of Prompts 1 and 2.
- **Disposition:** KEEP IN PLACE - code work remains.
- **Pruned:** nothing; pack header and deploy sentence re-stamped to cc0115b9e.
- **Evidence:** audited against 1d9cbf121. The only HSP source commit since 8ff701049 is cc0115b9e (Homestead Kit nails gate on the Metal Nail card); no plan claim depends on it. 25 perk files, 16 kit cards, 0 placement guards; wave0-reads.md and wave0-perk-runs.md absent (only wave0-r5-parked-stations.md exists, control). Deployed Homestead_Perks matches source by MD5. PlaythroughRow-Citations 13/13.
- **Verification debt:** T1.137 (PASS 276/276) and T1.138 (PASS 75/75), unrecorded.

### 2026-10-10 - MASTER_PLAN (HomesteadPerks round 2)
- **Verdict:** DRAINED. 0 open prompts: every build prompt (1-6, 9, 11-15) plus 16 (release closeout), 17 (Workbench reword, owner ruling 2026-10-10) and 18 (round b scenario fixes) landed; 44 perks and 34 kit items ship at 1.3.1 (unpublished; Nexus 1.2.4). 5 items awaiting verification, all with tracker rows. One non-code open decision: a generated Chest Kit / Wall Shelf Kit icon once OpenArt has credits (both ship vanilla `Shelf`).
- **Disposition:** ARCHIVE - moved to `Documentation/Design/HomesteadPerks_Round_2_As_Built.md` (it holds the Wave 0 R1-R5 runtime answers and the Considered table's rejection reasons); pack `MASTER_PLAN_Implementation_Prompts.md` deleted.
- **Pruned:** pack Prompts 16, 17, 18; plan "Remaining implementation" rows for them. Earlier prunes are listed in the plan's own status block.
- **Evidence:** ground truth is this session's own work rather than a separate /audit-plan pass: commits `f0ac1e6c4` (Prompt 17), `8290ed45f` and `a1c5ee25f` (Prompt 16 recount and audit fixes; Prompt 18 scenarios); `ls HomesteadPerks/CharacterPerk` = 44 files, `CardData/Item` = 34; /audit-perks, /audit-items, /audit-mod 2026-10-10 = 0 CRITICAL, Section H clean (`.audit/summary.md`); build 0 warnings; gates green (CardData-Integrity, PerkUid-AliasAndNameCollision, Perk-StartingItemStats, Sprite-NameResolves, Preflight-LocalizationParity, Localization-DuplicateKeys, Localization-EnglishColumnCjk, Preflight-MechChecks, Csproj-ContentIncludes, HomesteadPerks-WeightLines, Version-Synchronization, CardArt-ShippedForm, Deploy-Mods); Prompt 18 reruns on dev lane rig 12: T1.139 64/64, T1.142 106/106, T1.146 133/133, T1.147 168/168 PASS.
- **Verification debt:** T1.138 (owner ruled; the 75/75 run `t1-138-...-20261010-060711` meets the reworded EXPECTED, ready to record PASS); T1.139, T1.142, T1.146, T1.147 (reruns `...-20261010-105046`, `-105257`, `-105553`, `-110358` need a skeptic read). All `pending` in `.claude/playthrough-test-status.json`. Release still owed: framework 2.28.0, then HSP 1.3.1, `/package-mod-suite`, MUM last.

### 2026-10-11 - Fleet MASTER_PLAN (cross-mod decoupling), /cleanup-plans Fleet apply
- Scope: Fleet (cross-mod plan; the same entry is recorded in the ROADMAP of every mod it names: Community_Mod_Chest, AdvancedCopperTools, WaterDrivenInfrastructure, HomesteadPerks, HerbsAndFungi, Sirus23_Mod_Collection, CSFFModFramework).
- **Verdict:** DRAINED. Features 1-13 all built (1-3, 6, 7, 9, 12 in the commits `33ab18fe9`, `a3f965d43`, `76aac3d8a`, `cc0115b9e`, `fed7f7d0a`, `c6b2bad35`, `31034e212`, `e7b558ead`, `7219cd11d`), 0 open pack prompts, 0 unbuilt rows; every acceptance row recorded CONFIRMED (T2.789, T2.632, T2.997, T2.999, T2.998, T2.549, T2.557, T2.560, T2.1236 to T2.1242). Open question 4 settled by measurement, not by an owner choice.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Fleet_Cross_Mod_Decoupling_As_Built.md` with its Considered table and its drained prompt pack appended; the pack was deleted.
- **Pruned:** plan sections 1, 2, 3, 6, 7, 9 and 12 (each BUILT and confirmed), the stale "awaiting verification" pointers, open question 4's default text (replaced by its measured outcome); pack Prompts 9 and 10 (Prompt 10 had already been built in `e7b558ead`, the pack was stale on that point).
- **Evidence:** on dev lane rig 6 at HEAD (H&F, Harsh Wilds and Sirus23 deployed from the working tree, the rest synced from the real install), harness PASS for T2.557, T2.560, T2.632, T2.997 (both variants), T2.999 and T2.1236 to T2.1242; two independent skeptic reviews of all twelve rows (`.claude/handoffs/skeptic-review-2026-10-11-fleet/r-a.md`, `r-b.md`): 11 RECORD-PASS, and T2.560 FIX-SCENARIO (its brine leg dragged the non-draggable Liquid sub-card, so it passed on any build; fixed in the scenario and rerun PASS with a real refusal, "Dropping 'Pickle Brine' onto 'Wolf Companion' does nothing", beside the same container type with water running Give Water once). Three of the seven absent-partner pins had drifted since 2026-10-09 and were re-measured from real alone-boots (CMC 48 entries, five sprites now resolve; H&F plus twelve of its own map-node forward references; Harsh Wilds plus the Forest Lore `fl_mud_volcano` weight row; WDI now 0 lines), each entry checked against `Documentation/Interop_Registry.md`. End state: `grep BepInDependency(` over every `Plugin.cs` finds 13 files and none names anything but CSFFModFramework (the 13 matches are the control that the scan can hit). Open question 4 probe: with Sirus23 set aside the Wool-Quilted Cap and Vest each keep four requirement slots and the Wool one is null (measured), and a researched blueprint is left alone by `AcademyCourseService.HideWoolQuiltedIfNoWool` (read from code).
- **Verification debt:** none open. One unplayed fact, deliberately without a row: what a craft of an already-researched Wool-Quilted recipe asks for after Sirus23 is removed (the game reads a null slot as no requirement, the fleet audit's reading of the code); CMC's README states it as unplayed. Releases still owed per mod under its own version rules (`.claude/nexus-publish-status.json` against `ModInfo.json`, read 2026-10-11): framework 2.28.0 (Nexus holds 2.26.12), CMC 1.69.6 (1.68.45), ACT 1.18.1 (1.16.10), WDI 1.12.2 (1.11.3), HSP 1.3.1 (1.2.4), H&F 1.14.1 (1.13.7) and Sirus23 1.22.0 (1.21.6); MUM not repacked for this change.

### 2026-10-16 - MASTER_PLAN (vanilla balance pass and niche content)
- **Verdict:** DRAINED. Section 1 (HSP-B1 to B18) shipped in 1.3.5 (`4949f517a`). Section 2: the Well blueprint, Smallholding and the Snow Shelter gate were built in 1.3.6, the Mushroom Bed row had already closed as HSP-B18's text fix, and the part-built kits row was declined by the owner on 2026-10-16 ("the full built kits are wanted") after the research spike. The pack had Prompts 1-4; none are open.
- **Disposition:** ARCHIVE - moved to `Documentation/Design/HomesteadPerks_Vanilla_Balance_As_Built.md`, with a section 2 As-built record and a Considered table carrying the declined row and its reason. The pack `MASTER_PLAN_Implementation_Prompts.md` was deleted. The eight tracker rows citing the plan and the ACT plan's pointer now cite the archive.
- **Pruned:** pack Prompts 1-4 and its Awaiting list, whose rows T2.1738-T2.1742 are in the tracker. Plan section 2's idea table was replaced by As-built, Awaiting and Considered subsections.
- **Evidence:** this session's own build, not a separate /audit-plan pass. Prompt 1's files were started by session 460b276b (offline since 2026-10-15 18:30, mission hsp-plan-wave1) and checked here against `CSFFModFramework/Injection/BlueprintInjector.cs` (lookup from AllData), `CSFFModFramework/Data/WarpResolver.cs` (List append), `.decomp/TagOnBoardSubObjective.cs` and the EA 0.69c `Bp_Well.json` (all five unlock lists empty). Pester 63/63 across HomesteadPerks-Smallholding, -VanillaBalance, -WellBlueprint, -SnowShelterGate and -WeightLines, PerkUid-AliasAndNameCollision, Perk-StartingItemStats and Plans-Policy. The new gates' fixture breaks go red, and SnowShelterGate went red on the 1.3.5 bytes. `dotnet build -c Release` 0 warnings, 0 errors. Deployed: 45 perks, BlueprintTabs.json, 5 GameSourceModify files, zip `Homestead_Perks_1-3-6.zip`. Spike verdict FEASIBLE ONLY WITH C# (`Documentation/Research/HomesteadPerks/unfinished_kit_spike_2026-10-15.md`). **Not checked:** anything in a running game.
- **Verification debt:** T1.148 (Well blueprint), T1.149 (Smallholding), T2.1820 (Snow Shelter needs snow), T2.1738-T2.1742 (1.3.5 balance), all `pending`. HSP 1.3.6 is not on Nexus (Nexus holds 1.3.5).
