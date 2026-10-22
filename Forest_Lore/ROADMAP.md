# Roadmap: Forest Lore
Version at time of writing: 0.1.8
Date: 2026-10-20
Audit score: 10/10 (`.audit/summary.md` consolidated 2026-10-20; critical-analysis and mod-report both fresh at 0.1.8, code-quality's C# claim re-confirmed current)

## Current State

**Theme**: the forest's own landmarks, ruins and hidden places (Barrow Wood, the Old Battlefield, the Abandoned Homestead, the Mud Springs, Guano Rock, the Rock Shelter and the Sunken Caves) added to the vanilla map, for players who want more to find and read on the paper map without new items to manage. It hands out only vanilla cards.

**Content**: 0 items / 0 blueprints / 24 structures (CT2 location cards, `CardData/Location/`) and 3 improvements (CT10, `CardData/EnvImprovement/`) / 0 perks / 27 custom images (25 card pictures plus `FL_AxeRusty` and `FL_AxeRustyBlunt` for two artless vanilla axes). Also 9 map nodes, 13 improvement injections and 6 `GameSourceModify` patches.

**Stability**: 10/10, 0 CRITICAL, 0 design gaps, 0 open warnings (6 pre-flight warnings all adjudicated not-a-defect this run: C8 host-only plugin, F26 x3 intentional map-hidden chambers, M11 x2 axe art referenced via GameSourceModify). Critical-analysis and mod-report are now fresh at 0.1.8; three tracker rows pending, all old-save-fixture blocked.

**Open work**: no open retrospectives (`Documentation/Retrospectives/INDEX.md` has no Forest Lore row). No open plan: both plans are built and archived (`Documentation/Design/ForestLore_As_Built.md`, `Documentation/Design/ForestLore_Balance_And_Niche_As_Built.md`). The 0.1.8 Deer Lick encounter fix is harness-confirmed (T2.848 PASS), and T2.839/T2.841 now read PASS. Pending tracker rows: T2.847 (clone clay/peat parity), T2.1746 (Sunken Lake eel stock on an old save), T2.1748 (Homestead decay/repair/capacity), all needing a 0.1.4/0.1.6 save that visited the site, a fixture that exists on no rig.

**Framework compliance**: a 21-line `ContentModPlugin` host with the CSFFModFramework SoftDependency (2.28.0 or later). It uses no Tier 2 C# services because it has no C# behaviour; everything is framework data (MapNodes, ConnectionGates, InjectImprovementInto, GameSourceModify). No `DropCollectionGuardPatch`, no Harmony, no `ModLoaderVerison`.

---

## Phase 0: Stabilize

Skipped: audit score 10/10, no open retrospectives, no open CRITICAL or warning. The fresh 0.1.8 critical-analysis and mod-report retired the previous run's audit-freshness debt.

---

## Phase 1: Foundation

> Always do this phase. These are table-stakes for a healthy, maintainable mod.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Build the old-save fixture that T2.847, T2.1746 and T2.1748 need (a 0.1.4/0.1.6 save that already visited the Homestead, Sunken Lake and the clone sites), then record them through `/playthrough-test-plan`; until it exists the three rows stay fixture-blocked and cannot advance | Verification debt | P1 | Complex |
| Version hygiene: ModInfo.json, `Plugin.cs` and README are all 0.1.8 (re-read 2026-10-20); keep them in step on the next bump with `/update-mod-version` | Version hygiene | P1 | Quick |
| Localization: SimpEn.csv and SimpCn.csv hold the same 243 keys, Chinese parity CLEAN (re-read 2026-10-20); keep the same-commit parity on every new string | Localization | P1 | Quick |

---

## Phase 2: Core Expansion

No open plan holds new Forest Lore content, and the audits surface none. Any new site belongs in a new `Documentation/Plans/ForestLore/MASTER_PLAN.md`, checked first against the Considered tables of both As-Built records (they rule out, with reasons, Gnome Figurines at the Ossuary, a timed mud-warming action, an env-tag-lure or new-species Deer Lick, a scrapeable clay lick and `StripAllInheritedDrops` on the clones).

### Balance the declared placeholders
**What**: settle the Deer Lick odds (about 3 in 10 doe, 3 in 20 stag), the 6 percent relic finds at the Ossuary and the Bog Body, and the Stress -10 reading calm, all labelled placeholders in CHANGELOG 0.1.6/0.1.7.
**Why**: these are the only Forest Lore numbers no balance pass has judged against vanilla.
**Requires**: the method of `Documentation/Design/ForestLore_Balance_And_Niche_As_Built.md` section 0 (ledger rows and an owner decision page). T2.848 is now PASS, so the Deer Lick's behaviour is confirmed and only its values remain to tune.
**Complexity**: Medium

---

## Phase 3: Integration & Depth

No cross-mod hook is surfaced: the 2026-10-12 pre-flight found 0 foreign structural references and 0 tag collisions across 17 mods, and `Documentation/Interop_Registry.md` has no Forest Lore row.

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Custom art for the Weeping Pine | `fl_resin_pine` draws the vanilla `TreePine` sprite; `/create-image`, `/process-card-art`, then swap `CardImageWarpData` to the new PNG name (type 3) | Quick |
| Custom art for the Deer Lick | `fl_deer_lick` draws the vanilla `Tracks` sprite; same steps | Quick |
| Rusty Axe picture review | `FL_AxeRusty` was derived from `FL_AxeRustyBlunt` by painting a honed edge (no OpenArt in that session; sidecar `.art/FL_AxeRusty.json`); regenerate through `/create-image` if the `/review-art` gallery rejects it | Quick |
| Chamber item persistence | framework 2.28.0's keep-the-item fix is unseen in Forest Lore's chambers (none of T2.836 to T2.838 leaves an item on a chamber board); README's Known limitations says so. A tracker row that leaves an item in a chamber across a reload would close it | Quick |

### Not added: the Gnome Figurines (checked 2026-10-17)

The two Gnome Figurines (`TuskCarving_GnomeMale` 0c9ab5207f7b41b4896e2f9a3f86bc9f, `TuskCarving_GnomeFemale` 54c31356f6f93794fb964d028eae81a0) that `Documentation/Research/ForestLore/wave0-relic-finds.md` recommended for the Ossuary draw no art: `extract_vanilla_sprites.py card` on EA 0.69c (Python 3.13, UnityPy; control RusticTable -> Table) reports no Sprite reference for either. They are not added; adding them would mean shipping two art patches for a flavour find.

---

## Long-term Vision

> Where this mod should be at v1.0.

Forest Lore's natural endpoint is a finished set of verified, vanilla-balanced places: every site confirmed in a running game, every placeholder value tuned, every card with its own picture, and then maintenance mode. Its role is exploration and small, lore-flavoured rewards, not a new production chain, so 1.0 is reached by closing verification and polish rather than by adding systems. With the Deer Lick fix landed and confirmed, the remaining distance to 1.0 is the three old-save migration checks, the placeholder tuning pass, and four polish items.

**Potential major additions** (not yet justified - revisit after Phase 2):
- None surfaced by the audits; the ruled-out ideas live in the two As-Built Considered tables.

Any of these worth building belongs in the mod's plan under `Documentation/Plans/ForestLore/` (or its Considered table, with the reason, if it is not); never create a stub under `Documentation/Ideas/`, which keeps no backlog since 2026-10-01.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod ForestLore` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log`; re-check the vanilla indices the `GameSourceModify` patches address (gated by `ForestLore-BalanceGates` B13 and `ForestLore-BogBodyPatch`) |
| After fixing a critical issue | Run `/critical-analysis ForestLore` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo ForestLore` and bump the version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod ForestLore         - full health check, updates .audit/
/critical-analysis ForestLore - adversarial review
/code-quality ForestLore      - C# review (a bare ContentModPlugin host)
/build-mod ForestLore         - build Release DLL
/deploy-mods ForestLore       - build + deploy to game
/update-mod-version ForestLore <ver> - bump version in all 3 files
/export-to-repo ForestLore    - push to public repo
/create-image ForestLore <card> - card art for the two vanilla-sprite placeholders
```

---

## Plan Reconciliation Log

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 13 selected-table rows (P0 to P12) checked. 4 verified done (P0 and P1 by their commits; P2 and P6 by T2.830 and T2.834 PASS), 8 built-awaiting (P3 to P5 and P7 to P11 in the plan's numbering, tracker rows T2.831 to T2.833 and T2.835 to T2.841), 1 open (P12, owner-gated on Q9, no pack prompt). One STALE-CLAIM corrected: the plan's Selected table showed P11 as open although it is built (`04ce0d263`, T2.841 pending).
- **Disposition:** DRAINED (the pack has 0 open prompts; the plan stays in place because P12 waits on the owner's Q9 and ten checks are pending). The plan and pack were not deleted or moved.
- **Pruned:** from the plan, the "Next work" handoff narrative (built-commit list, release-closeout notes, verdicts so far, what-remains list), replaced by a short pointer section, and the resolved `WorldMap-CloneCreatorEnvLocalGuard` bullet under "Open calls". Nothing pruned from the pack: it was already refreshed on 2026-10-04 against `6b6952ad4` and holds only the awaiting list and the Q9 note.
- **Evidence:** all 20 commit hashes cited in the plan and pack resolve (`git cat-file -e`, 20 of 20; the probe was controlled by the same command rejecting nothing it should accept, and no hash failed). `ForestLore/` holds 9 `flEnv*` node UIDs in `WorldMap/MapNodes.json`, 22 `CardData/Location` files, 3 `CardData/EnvImprovement` files, `InjectImprovementInto.json`, `GameSourceModify/494b063d0a8ca66498dcd0e55b7f15be.json` and `Version` 0.1.0; `Development_Tools/Tests/ForestLore-BogBodyPatch.Tests.ps1` exists. 11 scenario files under `TestHarness/client/scenarios/FL/`; T2.833 is covered by the T2.832 scenario file (matched by grep). Tracker: T2.830 and T2.834 pass, the other ten pending (read from `.claude/playthrough-test-status.json`); all 12 T-ids in the plan and the pack exist there. `.claude/nexus-publish-status.json` has 0 ForestLore matches, so the unpublished claim holds. No code was run in a game.
- **Verification debt:** T2.831, T2.832, T2.833, T2.835, T2.836, T2.837, T2.838, T2.839, T2.840, T2.841 (all pending).

### 2026-10-07 - MASTER_PLAN
- **Verdict:** 13 Selected rows (P0 to P12): 5 verified done (P0 and P1 by their commits; P2 and P6 by T2.830 and T2.834 PASS; P12 by `nexus_publish.py status` reading page 53 `published` at 0.1.0, only a hand-pasted description left), 8 built and awaiting verification (P3 to P5, P7 to P11), 0 open code rows, 0 stale claims inside the plan. Pack: 0 open prompts (Prompt 20 done in `eda75ef44`), 9 prompts awaiting verification over 10 pending tracker rows; T2.833 CONFIRMED 2026-10-07. Q1 to Q14 and the four "Open calls" are all built on their defaults; none needs the owner. Two rows carry a harness FAIL not yet traced to a mod defect: T2.839 (the harness `SetStat` writes a delta from the capped Wetness, a harness defect) and T2.841 (every Climbing Aid read 0; decided by building the ladder through the real Improvements tab, where a 0 is a ForestLore fix against the archive's section 13). One stale claim outside the plan corrected: the Forest Lore rows of the coordinate reservation table in `Documentation/CSFF_Map_Travel_System.md` still read "reserved, not built" for nine nodes built since 0.1.0.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/ForestLore_As_Built.md` (the name its own section 15 named), text unchanged below a new header that carries the pack's per-row verification state; the pack (`MASTER_PLAN_Implementation_Prompts.md`, beside the plan) deleted, its content folded into the archive header; the ForestLore plan folder is now empty.
- **Pruned:** no plan section (kept whole as the as-built record, section numbers unchanged so every tracker row citing "section N" still points at the same text); the pack deleted whole after its "Awaiting verification" list, "Next action" and "Open calls" were folded into the archive header. Inbound path references repointed to the archive in `Development_Tools/Deploy-Mods.ps1`, `Documentation/CSFF_Map_Travel_System.md`, `Documentation/Ideas/IDEAS_OVERVIEW.md` (the index table), the FarTerritory, HarshWilds and WaterDrivenInfrastructure plans, two Research records, `ForestLore/Plugin.cs` and the T2.841 scenario; dated records (the plan-review snapshot, the IDEAS_OVERVIEW decision line, `git show <sha>:` paths) left as written.
- **Evidence:** 27 of 27 distinct commit hashes cited in the plan and pack resolve (`git cat-file -e`; control: `deadbeef1` rejected). `ForestLore/WorldMap/MapNodes.json` holds 9 distinct `flEnv*` UIDs at the section 1.2 cells, with `StripAllInheritedDrops` true on the three w 1 chambers only, and the Squeeze's Edge gate on `Encumbrance` (`21574a6120f4d3c4b913c69987e2ff06`) `LessOrEqual` 1000. `CardData/Location` 22 files, all CardType 2, `UniqueOnBoard` false except `fl_bog_body` (true); `CardData/EnvImprovement` 3 files, CardType 10, `BuildingDaytimeCost` 10/6/6; `InjectImprovementInto.json` 13 rows (11 stairs, 2 ladder ends); `GameSourceModify/494b063d0a8ca66498dcd0e55b7f15be.json` present and `Development_Tools/Tests/ForestLore-BogBodyPatch.Tests.ps1` 6 of 6 pass. Version 0.1.0 in `ModInfo.json`, `Plugin.cs` and README. 12 scenarios under `TestHarness/client/scenarios/FL/`. `nexus_publish.py status` (2026-10-07): ForestLore 0.1.0 `published`, ~1 line description drift. `Pack-Suite.ps1` packs it and `Mod_Update_Manager/SuiteModRegistry.cs` lists "Forest Lore (Pre-release)". `git log ea241d274..HEAD` over `ForestLore/`, the plan folder and the FL scenarios: 4 commits, none adding mod code after `eda75ef44`. Tracker: T2.830 and T2.834 pass, T2.833 confirmed, 10 pending. No code was run in a game.
- **Verification debt:** T2.831, T2.832, T2.835, T2.836, T2.837, T2.838, T2.839, T2.840, T2.841, T2.842 (all pending in `.claude/playthrough-test-status.json`). A FAIL that turns out to be a ForestLore defect opens a new ForestLore plan in the plan library, citing the archive's section.

### 2026-10-18 - MASTER_PLAN (vanilla balance and niche content)
- **Verdict:** every code row built. Section 1: FL-B1, B2, B3, B4, B9 in 0.1.5; FL-B13 in 0.1.7 through GameSourceModify patches on the clone location cards (`flLocHomestead` no Clay from Clear, `flLocMudSprings` Dig Peat 2), after the plan's premise (clay in Forage, strip the inherited drops) was corrected from the vanilla JSON. Section 2: N1, N2 Weeping Pine, N3, N4 in 0.1.6; the N2 clay-lick built in 0.1.7 as the Deer Lick on the owner's 2026-10-17 choice; the N3 follow-up found the Rusty Axe drew no art (fixed, 0.1.7) and the Gnome Figurines draw none (not added).
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/ForestLore_Balance_And_Niche_As_Built.md` with section numbers unchanged, the pack deleted and its awaiting list folded into the archive header; the ForestLore plan folder is empty again.
- **Evidence:** `Development_Tools/Tests/ForestLore-BalanceGates.Tests.ps1` 13 of 13 and `ForestLore-NicheContent.Tests.ps1` 24 of 24 (each with watched-red copies), `Art-ArtlessVanillaGrant` red on the unpatched Rusty Axe and green after; `dotnet build -c Release` clean. No code was run in a game.
- **Verification debt:** T2.1745 to T2.1748, T2.843 to T2.849 (all pending).
