# Roadmap: Herbs and Fungi
Version at time of writing: 1.14.2
Date: 2026-10-13
Audit score: 9/10 - PASS (0 CRITICAL, 0 DESIGN GAP, 7 WARNINGS, 6 MINOR) - see [.audit/summary.md](.audit/summary.md)

## Current State

**Theme**: The repo's gold-standard DATA mod - a deep foraging, herbalism and preservation system: 14 mushrooms, 9 berries/wild fruits, 7 medicinal herbs, teas and a mushroom broth, drying/pickling/fermentation chains, an Oil Press, an Apothecary Shelf, a Death Cap Ward, and 16 character-creation perks. Pairs with Community Mod Chest (Academy Herbalism course identifies Unknown Chanterelles).

**Content**: 249 items / 126 blueprints / 72 structures / 16 perks / 234 custom images / 45 liquids / 11 GameStats / 0 SelfTriggeredActions (counted 2026-10-14, after the 1.14.9 forest removal). No map locations: all forage is injected into vanilla locations.

**Stability**: 9/10 - release-ready. 0 CRITICAL, 0 DESIGN GAP. Clean Release build (0 errors / 0 warnings); versions synced 1.14.2 across ModInfo.json, Plugin.cs and README.md; localization EN/CN parity complete (3171/3171, 0 CJK-in-English, 0 stale). The adversarial critical-analysis (2026-10-13) returns SOLID with 0 failures. Open items are maintainability hardening, not defects: 5 code-quality warnings (silent-exit WarnOnce guards + one FieldInfo cache) plus a player-facing ModInfo under-claim and a bin/ tracking-hygiene item.

**Open work**: No open retrospectives name this mod (`sealablegates-portal-hardening-2026-07-02` is Graduated). The round-2 MASTER_PLAN was **drained and archived 2026-10-12** to `Documentation/Design/HerbsAndFungi_Content_Update_1_14_1_As_Built.md` (every feature built or closed to its Considered table); `Documentation/Plans/HerbsAndFungi/` no longer exists. A large awaiting-human-verification set (the T2.1xxx and T2.13xx rows, plus T4.41) remains open in `.claude/playthrough-test-status.json` - code work is done; those need a human at a running build. The mod sits at the repo root pending full human testing before any `Stable/` move (owner rule, 2026-10-12).

**Framework compliance**: Tier 2 - `ContentModPlugin` base, `Api.ActionRouter` for action dispatch (owns dedup), `Api.SpawnService.CardSpawned` for the Apothecary quest gate, `Api.TickEvents` for the mirror poll, `FindObjectsOfTypeAll` load-time one-shots cached and type-guarded. All event registration is once-per-process from `RegisterPatches` (not `OnGMInitialized`), so a save reload cannot stack handlers. Soft dependency on `crispywhips.CSFFModFramework`. No deprecated patterns (no `DropCollectionGuardPatch`, no hot-path prefixes, no manual perk/blueprint injection, no `ModLoaderVerison`/`ModEditorVersion`).

---

## Phase 0: Stabilize  *(skipped - audit score >= 8, no open retrospectives)*

---

## Phase 1: Foundation

> Maintainability hardening and hygiene. None changes behaviour in the normal case; each closes a silent-failure or drift gap that would only bite on a future game-version rename. All are from the 2026-10-11 code-quality review, re-confirmed present in current source by the 2026-10-12 mod-report.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Pickle gate: `WarnOnce` when `ContainedLiquid` field is null (`PickleVatRoutePatch.cs:136-142`); cache the `FieldInfo` per vat type. A rename would silently disable every pickle blueprint + Spice Brine, logged only at Debug. | Reliability (W1) | P1 | Quick |
| Forager's Notebook: resolve `CustomDesc` explicitly, `WarnOnce` and skip instead of `?? ""` (`ForagersNotebookPatch.cs:116-117`). A rename would overwrite saved player entries with one species. | Reliability (W3) | P1 | Quick |
| "Dig for Truffles": count the three shape-drift exits and `LogWarning` once when `added == 0` and locations resolved (`GameLoadPatch.cs:451,454,457`). | Reliability (W2) | P1 | Quick |
| Both `MakeBrine` catches: log `ex.InnerException?.ToString() ?? ex.ToString()` (`PickleVatRoutePatch.cs:159,192`), matching the rest of the mod. | Diagnostics (W4) | P2 | Quick |
| Hoist the four `AccessTools.Field` lookups out of the 51-location loop and use `CachedField` (`GameLoadPatch.cs:445,450,467,468`). | Performance/noise (W5) | P2 | Quick |
| Add one sentence to `ModInfo.json` Description naming the 1.14.x wild finds (Watercress/Cress Bed, Water Lily Rhizome, Horseradish/Chicory/Goosefoot, Mandrake, Rhubarb, Mustard, Iced Teas/Shaved Ice, fire-roasted Peanuts) at the next Description edit. | Feature honesty (W6) | P2 | Quick |
| Untrack `bin/Release/` (231 stale tracked files; the folder's own .gitignore already ignores it; source + BinSync are clean). | Repo hygiene (W7) | P3 | Quick |

---

## Phase 2: Core Expansion

> No active plan. The round-2 MASTER_PLAN (67 features) was drained and archived 2026-10-12. Every feature is either built (48 sections, shipped across 1.14.0-1.14.2, awaiting human verification) or closed into the archive's "Considered, not selected" table with its reason. **Do not re-promote a Considered row** - each carries why it was ruled out (vanilla already ships it, dead mechanism, design call). New content for this mod starts from a fresh `/generate-ideas` + plan, not from the archive's Considered table.

- **Shipped, awaiting human verification** (code done; the only "expansion" left is confirming it plays): the T2.1xxx / T2.13xx rows in `.claude/playthrough-test-status.json` - Add-to-Stew absorption, honey syrup, gruit/needle/mulled ales, Forager's Seasoning, Bath Soak, Unknown Chanterelles, and the rest. Source of truth: `Documentation/Design/HerbsAndFungi_Content_Update_1_14_1_As_Built.md`.
- **2026-10-14 (1.14.9): the Foraging Forest is retired.** Owner decision once Far Territory took over mod-made locations: the 7 forest nodes, the Wild Bees' Nest, the three forage hotspots, the Fungal Bloom, the Overgrown Forest Trail and the Old-Growth Grove unlock are removed, and every forest forage rule moved to the vanilla location its node cloned (see CHANGELOG 1.14.9). Any roadmap, audit or tracker row about those features (for example T2.1007, the Wild Bees' Nest) no longer applies and is not work to do.
- **No new content is scheduled.** The mod is feature-complete for round 2; the next build phase would be a new planning pass, not continuation of a drained plan.

---

## Phase 3: Integration & Depth

> The cross-mod hooks are already shipped; what remains is partner-side acceptance, tracked in the partner mods, not here.

### Academy Herbalism course (Community Mod Chest)
**What**: CMC's Academy Herbalism course identifies Unknown Chanterelles. Already wired via `AcademyHerbalismBridgePatch`.
**Why**: gives CMC players a progression route to safe chanterelle ID beyond the Herbalism skill and the Botanical Field Guide.
**Requires**: CMC installed (soft). Shipped.
**Complexity**: done.

### WDI sawdust -> Grow Bag; Sirus23 `tag_x_AnimalRemedy`
**What**: WDI's Sawmill "Cut" drops vanilla `WoodShavings`, which H&F's Grow Bag accepts; H&F self-tags an animal remedy that Sirus23's livestock feature consumes.
**Why**: completes the forager/industry and forager/livestock loops across mods under the soft-dependency doctrine (consumer ships the acceptance).
**Requires**: coordination recorded in the As-Built; a couple of partner-side legs were still blocked outside the plan at close (see the archive's Considered/cross-mod rows).
**Complexity**: H&F half done; remainder is partner-mod work.

---

## Phase 4: Polish

> Art is effectively complete - 237 custom PNGs, 292 image references all resolve, 0 orphans, 0 GUID-in-sprite. The 17 unlinked images the 2026-10-11 critical-analysis flagged are now wired.

| Item | What | Complexity |
|------|------|------------|
| Perk starting-item weight balance | 10 perks exceed the 8-entry weight-bomb heuristic (all WarpData resolves; counts match descriptions). In-game feel check only, filed as tracker row T4.41. Not a bug. | Quick (human check) |
| Description-accuracy pass | Fold the ModInfo under-claim fix (Phase 1 W6) into the next player-facing text sweep; confirm README/CHANGELOG/ModInfo stay in sync. | Quick |

---

## Long-term Vision

> Herbs and Fungi is feature-complete for its current scope. Its natural endpoint is maintenance mode: it already occupies the full early-to-mid-game foraging, herbalism and preservation niche, and the round-2 plan deliberately drained the backlog ("reach an end with the mod development and then shift into maintenance mode"). The next milestone is not more content but a full human playthrough of the large awaiting-verification set, after which the mod moves into `Stable/` and only updates to track game versions and player reports.

**Potential major additions** (not justified by current scope - revisit only after the awaiting-verification set clears and only via a fresh planning pass):
- A dedicated Herbalism progression arc tying the skill, the Botanical Field Guide and the Academy course into a longer identification/mastery loop - fits the mod's herbalism theme but adds a system, not just cards.
- Cross-mod flavour-synergy pairs with CMC's `cmcflavourtannic` (the mushrooms' Tannic/Earthy pairing) - currently handed back to CMC's plan to avoid a foreign-reference boot warning; revisit if H&F ever ships its own FlavourTag.

Any of these worth building belongs in a fresh plan under `Documentation/Plans/HerbsAndFungi/` (or its Considered table, with the reason); never a stub under `Documentation/Ideas/`, which keeps no backlog since 2026-10-01.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod HerbsAndFungi` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp.dll`, re-run `/diagnose-log` |
| After fixing a Phase 1 item | Run `/code-quality HerbsAndFungi` to confirm the warning cleared |
| Before the `Stable/` move | Complete the full awaiting-verification playthrough set; the mod moves only after full human testing (owner rule) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod HerbsAndFungi          - full health check, updates .audit/
/critical-analysis HerbsAndFungi  - adversarial review
/code-quality HerbsAndFungi       - C# reliability/maintainability scan (Phase 1 source)
/repair-items HerbsAndFungi       - auto-fix item JSON issues
/build-mod HerbsAndFungi          - build Release DLL
/deploy-mods HerbsAndFungi        - build + deploy to game
/playthrough-test-plan status     - the awaiting-verification set
/export-to-repo HerbsAndFungi     - push to public repo
```

---

## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b. Append-only: never reword, reorder or delete an entry.
> `/roadmap` Step 7 preserves this section verbatim when it overwrites the rest of this file.

### 2026-09-07 - Audit_Remediation_Plan
- **Verdict:** 0 of 12 feature rows built (N1-N12). The Fixes section is empty by design, not by
  omission: 0 open CRITICAL and 0 open Design Gaps at promotion, all closed in v1.10.18.
- **Disposition:** KEEP IN PLACE. Twelve rows of real code work remain, so the plan correctly stays
  in `Documentation/Plans/HerbsAndFungi/`.
- **Pruned:** nothing. No row proved done, so no plan section and no pack entry was removed. No
  `_Implementation_Prompts.md` pack has ever been generated for this plan.
- **Evidence:** UID probe across the 214 live UniqueIDs in `CardData/` + `CharacterPerk/` returned 0
  hits for all 12 rows; a control probe was run FIRST and returned `truffle` 7, `broth` 2, `yarrow` 4,
  `butter` 4, `perk` 15, proving the probe can hit. Cross-checked differently in kind:
  `git log --diff-filter=A --since=2026-09-05 -- HerbsAndFungi/` returns no added files, and
  `git status --porcelain -- HerbsAndFungi/` is clean (no uncommitted work in flight). Spot checks on
  the rows easiest to misread: exactly 4 `*_ground` UIDs remain (the medicinal ones), so N1's culinary
  powders are absent; only `herbs_fungi_bp_mushroom_broth` + `herbs_fungi_mushroom_broth` exist, so
  N2's three variants are absent; still 15 perks, every one with `StartingStatModifiers: []` and no
  passive modifiers, so N3 is absent; still the original 4 `Bp_OilPress_HerbalOil_*` files, so N5 is
  absent. `ModInfo.json` remains 1.10.18. The one intervening commit, `8cc360ebd`, repaired Chinese
  leaking into the English CSV column and is not a plan row.
- **Verification debt:** T2.171, T2.172 and T2.118 all present in
  `.claude/playthrough-test-status.json` (321 rows across its `items`/`confirmedItems`/`droppedItems`
  maps), all `pending` on a human in-game check. Plan-lifecycle gate 2 is therefore satisfied.
- **Blockers a future implementer must not step on:** N6 depends on N9 by the plan's own note, and
  N12 is explicitly gated pending UID/name ownership against Community_Mod_Chest's existing
  "Wild Garlic".

### 2026-09-08 - Audit_Remediation_Plan (implementation pass)

- **Verdict:** 12 rows at the start of this pass, 3 remaining. 6 rows BUILT (N5, N3, N9, N1, N2, N8),
  3 rows DROPPED as already shipped by vanilla (N4, N7, N12), 1 row BLOCKED on human verification
  (N6), 2 rows still open and buildable (N10, N11). The prompt pack went 12 open to 3 open and its
  header counter was recomputed from its own body at each step, not hand-edited.
- **Disposition:** KEEP IN PLACE. Real code work remains (N10 dried-herb storage container, N11
  herbal incense bundle), so the plan correctly stays in `Documentation/Plans/HerbsAndFungi/`. It is
  NOT archivable yet.
- **Pruned:** the 6 built rows and the 3 dropped rows were removed from both the plan's Features
  table and `Audit_Remediation_Plan_Implementation_Prompts.md`, each by an anchored patch that
  asserted its target matched exactly once before writing and re-read the file afterwards. The 3
  dropped rows were relocated to `Documentation/Ideas/HerbsAndFungi/IDEAS.md` under a new
  "Ruled out - vanilla already ships this (do not re-promote)" section CARRYING their evidence, and
  the 2 shipped rows that were still sitting in that file's "Near-Term (ready to build)" list were
  moved to its "Shipped" section. That relocation is the load-bearing half: `/audit-to-plan` promotes
  from Near-Term, so leaving them there would have re-promoted dropped work as ready.
- **Evidence:** every row was verified against the vanilla export at
  `Documentation/GameData/CSFF-JsonData_Current/UniqueIDScriptableGUID/CardData.json` before or after
  building. N7 dropped because `Bp_Twine` is ungated (`CardsOnBoard` empty, `BlueprintUnlockTicksCost`
  0.0, 4 Fibers to 4 Twine) alongside `Bp_TwineSpinningWheel` and a self-trigger CI on Fiber, so the
  fiber loop was already closed end to end. N12 dropped because vanilla ships `WildGarlicFresh`,
  `WildGarlicDried`, `WildGarlicCooked`, `WildGarlicPowder`, `LQ_WildGarlicPowder`, `LQ_GarlicWater`
  and a three-stage planting chain. N4 dropped because vanilla ships `BracketFungusFresh`,
  `BracketFungusDried`, `BracketFungusDried_Lit` and `BracketFungusStrips`. Build was re-run green
  (0 warnings, 0 errors) after every single change; `Development_Tools/Check-LocalizationParity.ps1`
  went 1283/1283 CLEAN at baseline to 1376/1376 CLEAN at the end, every step matching added EN keys
  to added CN keys exactly; `PerkUid-AliasAndNameCollision.Tests.ps1` passes 9/9 with the new perk in
  place. A twine implementation WAS built and then removed the same session once vanilla redundancy
  was found: parity returned to exactly its pre-twine value of 1309/1309 and `BlueprintTabs.json`
  dropped out of `git status` entirely, which is what proved the revert was byte-clean rather than
  merely plausible.
- **Corrections to the 2026-09-07 entry above (which is left unedited, this log being append-only):**
  that entry's final bullet records two blockers, and BOTH are refuted. It says N12 is "gated pending
  UID/name ownership against Community_Mod_Chest's existing Wild Garlic" - CMC does not own Wild
  Garlic; vanilla does, and CMC merely consumes it, injecting `WildGarlicFresh` into forage at 15%
  and 8% in its own `DropInjections.json`. It also says "N6 depends on N9 by the plan's own note" -
  N9 is Flax/Linseed Oil and is unrelated; N6's real prerequisite is an in-game confirmation of the
  HempButter OnZero chain, which is tracked as T2.153 and is still `skip`.
- **A systematic defect in how this plan was promoted, recorded so the next promotion avoids it:**
  the 2026-09-05 pass verified all 12 rows were ABSENT FROM THE MOD and never checked whether VANILLA
  already provided them. Three of twelve did. One of those three (N7) was fully built before the gap
  was noticed. Any future `/audit-to-plan` run against this mod should probe the vanilla export per
  row before sequencing it as ready work.
- **Two row premises were also false and are corrected in the tracker rather than in the deleted plan
  text:** N1 claimed its powders would feed a Pouch flavour-transfer "already wired for the medicinal
  grounds" - that transfer is gated on `LiquidTransferRules.SolidToLiquid[]`, a hardcoded Unity asset
  no mod JSON can reach, vanilla has 11 `LQ_*Powder` counterparts while this mod's 4 medicinal grounds
  have zero, and `CurrentLiquidQuantity`/`SolidToLiquidInfo` exist in no vanilla export and no mod
  JSON. N5 specified DRIED herbs as the oil-press input when all four shipped herbal oils consume the
  FRESH herb. Both corrections live in T2.209 and T2.188 so a tester is not sent to check the wrong
  thing.
- **Verification debt:** six rows filed in `.claude/playthrough-test-status.json`, all `pending` on a
  human at a running build - T2.188 (herbal oils), T2.189 (Herbalist's Advantage), T2.208 (linseed
  oil), T2.209 (mushroom powders), T2.210 (buff broths), T2.211 (berry preserve). T2.153 (HempButter
  chain) remains `skip` and is what blocks N6. Plan-lifecycle gate 2 is satisfied for everything
  shipped in this pass.
- **Out-of-plan fix shipped alongside:** `PeanutOil.json` referenced SpiceTag
  `herbs_fungi_spice_peanut_oil`, which had no backing file, so that oil's spice contribution was
  silently inert. It was the only one of the mod's four spice-tag references that did not resolve.
  Created with Peanut Oil's own flavour profile; dangling count is now 0.

### 2026-09-08 - Audit_Remediation_Plan (implementation wave 2)

- **Verdict:** 3 rows at the start of this pass, 1 remaining. 2 rows BUILT and shipped as v1.12.0
  (N10 Apothecary Shelf, N11 Herbal Incense Bundle); N6 unchanged and still BLOCKED. The pack went
  3 open to 1 open and its header counter was recomputed from its own body, not hand-edited.
- **Disposition:** KEEP IN PLACE, but PARKED rather than active. The single surviving row is real
  code work, so plan-lifecycle gate 1 is not satisfied and the doc cannot be archived - but no agent
  can start it, because its prerequisite is a human in-game check (T2.153, still `skip`). Anyone
  opening this plan looking for work should expect to find none.
- **Pruned:** the 2 built rows from the plan's Features table and Prompts 8 and 11 from the pack,
  each by an anchored patch that located its target by a unique token, read the anchor text OUT of
  the file rather than retyping it, asserted an exactly-once match before writing, and re-read
  afterwards. Two stale fragments left by the wave-1 prune were also repaired: an orphaned sentence
  in the Features intro, and a pack warning about Prompts 9 and 12 colliding on `GameLoadPatch.cs`
  when neither prompt still exists.
- **Both rows were re-probed against VANILLA before authoring**, per the rule wave 1 had to learn
  the hard way. Neither was redundant: the export ships no incense, censer, smudge, sage or perfume
  at all, and every vanilla container (`Shelf`, `Bookshelf`, `Chest`, `Basket`, `ClayStoragePot`,
  `RusticBarrel`) ships EMPTY `TagFilters`, so no vanilla container filters by tag.
- **N10's own "Corrected on verification" clause was WRONG, and following it would have shipped a
  container that filters nothing.** It directed the implementer to filter on `tag_Herb` plus
  `tag_Preservable`. `.decomp/CardFilter.cs` `SupportsCard` collects positive `TagFilters` into one
  list and breaks on the FIRST match, so positive tag filters are OR, not AND; `tag_Preservable`
  alone sits on 103 of this mod's cards, so that pair would have accepted 109 of about 120. Shipped
  instead as OR(`tag_Herb`, `tag_Fungus`, `tag_Powder`, `tag_Medicine`), reaching 56. **That is the
  third row in this one plan whose stated premise was false on inspection** (after N1's
  flavour-transfer claim and N5's dried-vs-fresh ingredient in wave 1), which is a consistent enough
  rate that a promoted row's guidance should be read as a hypothesis to test, not an instruction.
- **A second-order fact no row mentioned:** `tag_Herb`, `tag_Fungus` and `tag_Medicine` do not exist
  in vanilla at all (checked against the 382 entries of `ScriptableObjectObjectName/CardTag.txt`),
  so WarpResolver creates them at load. The shelf therefore accepts THIS mod's herbs and mushrooms
  and is not expected to accept vanilla ones. Written into T2.225 so a tester does not file it.
- **N11 needed neither C# nor the documented fallback.** Its prompt allowed shipping a personal
  effect if room-scale delivery could not be proven. It could be: `PassiveEffects[].StatModifiers[]`
  under `Conditions.NotInBackground` reaches player GameStats from a card merely on the board -
  vanilla `BasketPlaced` grants Sanctuary +50 that way and CMC's lit incense burner drains Stress at
  -0.35 the same way. Pure JSON: no Harmony patch, no `Api.TickEvents`, no poll.
- **Evidence:** build 0 warnings 0 errors after every change; `Check-LocalizationParity` went
  1376/1376 CLEAN to 1412/1412 CLEAN, +36 EN matched by +36 CN exactly; all six new cards, the new
  sprite, both CSVs and both `BlueprintTabs.json` registrations confirmed present in `bin/Release`;
  the tracker insert measured +18/-0 in `git diff --numstat`. Every Chinese string was assembled
  programmatically from chunks sliced out of already-shipped `SimpCn.csv` text (this mod's plus the
  sibling mods', which already translate an incense burner), and the builder asserts each string
  DECOMPOSES entirely into those shipped chunks - a check watched rejecting both an unsliced chunk
  and two invented characters, and accepting a genuine composition, before it was trusted.
- **Verification debt:** T2.225 (`hf_apothecary_shelf`) and T2.226 (`hf_incense_bundle`), both
  `pending`. T2.226 records that the ambient `PassiveEffects` shape has never been watched working
  by anyone - CMC's identical shape is T2.166 and is still `skip` - so it is a first observation,
  not a re-test. T2.225 records that it shares the open T2.175 weight-mode-container question and
  should be checked in the same session.
- **Docs drift repaired while here, all of it inherited from wave 1 and all player-facing:**
  `ModInfo.json` and README both claimed 15 character-creation perks when 16 ship; the README perk
  table was missing Herbalist's Advantage entirely; the README Cooking tab row omitted the three
  broths and Berry Preserve; and the README Version History still read "v1.10.18 (current)" three
  versions late, with no v1.11.0 entry at all.

### 2026-09-08 - Audit_Remediation_Plan (implementation wave 3, plan drained and archived)

- **Verdict:** 1 row at the start of this pass (N6 Truffle Butter + Truffle Salt, marked BLOCKED),
  0 remaining. N6 BUILT and shipped as v1.13.0. The block was re-derived before being accepted and
  did not hold: the prompt told the implementer to clone the Hemp Butter heat-activate-then-solidify
  chain and wait for its in-game confirmation (T2.153, still `skip`), but a compound butter (fat +
  truffle) and a finishing salt (salt + dried truffle) need no activation and no drying timer. The
  shapes actually cloned are the mod's own `HempFlowerPowder` "Mix with Fat or Butter" drag action
  (four vanilla fat GUIDs as triggers, both ways, given card destroyed, receiving card transformed)
  and the `RoastedPeanuts`/`CutDriedTruffle` CI family. 12 of 12 original rows are now resolved: 9
  built (v1.11.0 six, v1.12.0 two, v1.13.0 one), 3 dropped as vanilla-redundant.
- **Disposition:** ARCHIVE. Plan-lifecycle gate 1 (no code work remains) and gate 2 (every
  awaiting-verification item has a tracker row) are both satisfied. The plan doc and its pack were
  moved verbatim, with a final Promotion Log entry and the pack header recomputed to 0 open, to
  `Documentation/Design/HerbsAndFungi_Audit_Remediation_As_Built.md` (plan first, pack as an
  appendix, matching the CMC/ACT/WDI archives). The 8 tracker rows whose `source` cited the old plan
  path (T2.188, T2.189, T2.208-T2.211, T2.225, T2.226) were repointed at the archive in the same
  change; the dated entries above that name `Documentation/Plans/HerbsAndFungi/` are records of
  what was true then and were left alone. This record was written BEFORE the plan files were
  removed.
- **Pruned:** row N6 from the plan's Features table and Prompt 10 from the pack, each by an anchored
  patch asserting an exactly-once match and read back afterwards; the drained table keeps its header
  with a one-line pointer to the Promotion Log. `IDEAS.md` Near-Term is now empty (annotated so
  `/audit-to-plan` does not promote from a blank list) and the N4 idea moved to its Shipped section
  CARRYING both of its false premises (the clone source, and "for the Pouch system", which has no
  solid-side flavour transfer to feed).
- **Out-of-plan fix shipped alongside, found by reading the row's own acceptance clause:** every
  one of this mod's 72 flavoured cards encoded `FlavourTags[].Intensity` on an ordinal 1-7 scale,
  while the engine field is the enum `FlavourIntensities` (0 Medium, 1 Strong, 2 Subtle) read through
  `FlavourSynergyRules.GetNumberForIntensity`, whose default arm returns 0. So 91 of 205 entries
  (every value 3-7: the truffles, the dried and cooked mushrooms, the concentrated berries)
  contributed no flavour and printed no flavour line, while 1 read as Strong and 2 as Subtle.
  Remapped in place across 70 files plus `SpiceTag/Spice_PeanutOil.json` (1,2 -> Subtle; 3,4 ->
  Medium; 5-7 -> Strong; the three already-enum spice files unchanged), by a regex that touched only
  the `Intensity` digits (asserted: stripping those digits from before and after yields identical
  text). Threshold choice is recorded in the CHANGELOG; the one independent check the data offers
  agrees with it (`Spice_HempOil`, already enum-valued as Nutty Medium / Earthy Subtle, matches what
  the remap produces from its card's ordinal Nutty 3 / Earthy 2).
- **Truffle Salt deliberately has no mortar requirement.** `RequiredTagsOnBoard` on a card action
  (`TagOnBoardCondition[]`) is used by no vanilla card action and no mod in this repo, so its JSON
  shape cannot be verified offline, and a mis-shaped condition leaves `TriggerTag` null and hides
  the action forever rather than merely dropping the requirement. It is a hand grind costing one
  time unit; recorded in the CHANGELOG, README and T2.230 so nobody files the missing tool as a bug.
- **Evidence:** vanilla-redundancy probe first (the export has no truffle, truffle butter or truffle
  salt; it ships `MilkButter`/`ButterChunk`, whose sprite name `Butter` the new card reuses).
  `.decomp/FlavourIntensities.cs`, `FlavourSynergyRules.cs`, `FlavourAndIntensitySetup.cs`,
  `InGameCardBase.cs` (flavour seeding and the description builder) and the framework's
  `JsonDataLoader.cs` (`JsonUtility.FromJsonOverwrite`, raw int into the enum) read for the remap.
  Build 0 warnings 0 errors after every change, run twice (the second after the ModInfo description
  edit); `bin/Release` ModInfo, both CSVs, `Truffle.json` and a remapped card confirmed byte-identical
  to source. `Check-LocalizationParity` 1412/1412 CLEAN to 1430/1430 CLEAN, +18 EN matched by +18 CN
  exactly; every CJK chunk in the 18 new Chinese values was sliced from the pre-edit `SimpCn.csv` or
  the vanilla export and asserted present before assembly. New gate
  `Development_Tools/Tests/FlavourTags-IntensityEnum.Tests.ps1` 5/5, including its red half on a
  broken fixture that names the offending index. Tracker edit was text-level (+28/-10, the 10
  replaced lines all mine to replace: 8 repointed sources, one note, one timestamp) because a
  re-serialise would have reformatted rows a peer wrote with deeper indentation. Full workspace
  suite result is in the commit message for this pass.
- **Verification debt:** T2.230 (`hf_truffle_butter_and_salt`, both items plus their flavour lines)
  and T2.231 (`hf_flavour_intensity_enum_remap`, three named cards and one stew), both `pending` on
  a human at a running build. T2.153 (Hemp Butter activation chain) stays open on its own merits and
  its note now says nothing depends on it. All nine shipped plan rows remain unconfirmed in-game:
  T2.188, T2.189, T2.208-T2.211, T2.225, T2.226, T2.230, T2.231.

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

### 2026-09-17 - Trait_Effect_Repair_Plan (closeout)
- **Scope:** Fleet. The same entry is in `Community_Mod_Chest/ROADMAP.md`, `HerbsAndFungi/ROADMAP.md`, `Sirus23_Mod_Collection/ROADMAP.md` and `CSFFModFramework/ROADMAP.md`, the four mods the plan names.
- **Verdict:** every phase built: P1 (CMC 1.68.25), P1b (1.68.26, `1f71be4a7`), P1c (1.68.27, `b5d93a885`), P7 (CMC 1.68.28 and HerbsAndFungi 1.13.1, `e4fad82ab`), P2-P6 (CMC 1.68.30, Sirus23 1.21.3, CSFFModFramework 2.26.0, `7b1c68f64`), and the pack's last open prompt, Prompt 1 (delete the temporary `Community_Mod_Chest/Patcher/TraitDiagnostics.cs` tracer), in `8d25ed810` with no version bump. Prompt 1 was gated on playthrough row T2.239, which is still pending; the owner lifted that gate on 2026-09-17 ("We need to be able to proceed with code work without being blocked on a full playthrough"). The plan doc's own row inventory was compared against the pack rather than trusting its 0-open count, and three plan items with no recorded outcome were settled this pass, none needing code: section 3.6's pre-release check (no `TriggerRange` on the four infection stats Deadly Disease rate-modifies), section 3.2's Swimmer 5 Sun re-check (kept), and section 3.1's "drop madness from Lunacy's text" (already done in 1.68.30). 0 plan rows unbuilt.
- **Disposition:** ARCHIVE to `Documentation/Design/Trait_Effect_Repair_As_Built.md`: the plan with an archive banner, dated notes for Prompt 1 and the three checks above, and the drained pack appended as an appendix with Prompt 1 removed. Pack `Documentation/Plans/Fleet/Trait_Effect_Repair_Plan_Implementation_Prompts.md` deleted. The plan holds the only record of the ground truth behind the rework (R1-R20, D1-D9) and of every deviation from its starting values, so it is kept, not deleted. Not published: the public releases are still CMC 1.68.24, HerbsAndFungi 1.13.0, Sirus23 1.21.2 and CSFFModFramework 2.25.32 (`.claude/mod-publish-status.json` commits read back through each `ModInfo.json`), and the export is the owner's call. R14/R16's reading of the player's "+1.2 Speed" as the +1.2 Aid rate is still unconfirmed with the player; it is recorded in the archive's section 1.
- **Pruned:** Prompt 1 from the pack before the pack was folded into the archive and deleted; nothing else. The plan left `Documentation/Plans/Fleet/` whole.
- **Evidence:** Prompt 1: `dotnet build -c Release` 0 warnings 0 errors; the built `bin/Release/Community_Mod_Chest.dll` holds `TraitDiagnostics` (UTF-8) 0 times and `[TRAITDIAG]` (UTF-16-LE) 0 times, against controls `RiverSwimPatch` 1 and `[RiverSwimPatch]` 9; `grep -rn TraitDiagnostics --include=*.cs Community_Mod_Chest` 0 lines (control `RiverSwimPatch` 15); no file under `Documentation/Retrospectives/` cites `TRAITDIAG` or `EnableTraitDiagnostics`. Plan rows against code: `Community_Mod_Chest/GameStat/` holds all nine `CMC_Trait*.json` (eight trait stats plus `CMC_TraitSkinShelter.json`); `TraitsTickHandler.cs`, `TraitsActionHandler.cs` and `TraitDiagnostics.cs` absent from `Patcher/`; gates (a)-(e) plus the extended river swim test present as `TraitStat-CompositeBands`, `Perk-HeldTestNotAllPerks`, `PerkAidRate-HoldsTier`, `PerkStatClamp-Reachability`, `StatBase-NoVanillaSource` and `CMC-RiverSwim` `.Tests.ps1`; `CSFFModFramework/Patching/PerkOriginTagPatch.cs` and `Discovery/ModTag.cs` present, `[Perks] ShowModOriginTag` bound at `CSFFModFramework/Plugin.cs` and documented in its README config table; 12 `ModInfo.json` files carry `ShortName`; 21 Sirus23 JSON files reference `SaturationDairy` (by name or its UID `f4b08d0250e6099419e010a83578b9db`); `ModInfo.json` versions CMC 1.68.30, HerbsAndFungi 1.13.2, Sirus23 1.21.3, CSFFModFramework 2.26.0. Section 3.6 check: a JSON walk of 29,435 files (the vanilla EA 0.67i UniqueID and ScriptableObject exports plus every mod folder, 0 unparseable) found 0 `TriggerRange` objects whose `StatWarpData` is Infection_Gastrointestinal `1a8d37787d69c9b4aa05d332921f3763`, Infection_UpperRespiratory `3407bfc804966194e9e369a7cce6d07d`, Infection_Systemic `dc3cae53109fd5945b5279ec6291caae` or Infection_LowerRespiratory `fa47d156a14dac842bcdcbdbf8504e35` (by UID or name), with the same walk finding the control, vanilla `Tgr_Anxiety` on Stress at 240; `.decomp/` (949 `.cs`) names no infection stat (controls `HourOfTheDayValue` 4 files, `StatValueTrigger` 13), and no mod `.cs` references the four UIDs. Section 3.2: vanilla `CharacterPerk` SunsCost is only ever 0, 15 or 30 (71/38/20 of 129), and Swimmer's 5 matches the six other five-Sun CMC perks (CMC's SunsCost counts: 0 x20, 1 x21, 5 x7, 15 x5, and 10, 20, 25, 30, 100 once each), which include Abundant Growth's +1.2 Aid rate and Wide Hands' +15 skill offset; Swimmer now delivers what its price was set for. Section 3.1: `madness`, `insan` and `mania` absent from `Pk_Lunacy.json`, `CMC_TraitLunacy.json` and its CSV rows. Tracker: T2.239 and T1.84-T1.114 all present in `.claude/playthrough-test-status.json`, all `pending`, so lifecycle gate 2 holds; the 33 plan and pack path citations in it and the one in `Playthrough_Test/Playthrough_Checklist.html` were repointed at the archive in the same commit.
- **Verification debt:** T2.239 (Nyctophobia and the tracer's log lines), T1.84-T1.88 (P1b), T1.89-T1.97 (P1c), T1.98-T1.101 (P7 medicine), T1.102-T1.109 (P2 condition traits), T1.110-T1.111 (P3 swim and Aid), T1.112 (P4 Sirus23 dairy), T1.113-T1.114 (P5 origin tag and clock hour), all `pending`. T2.239 part (5), T1.114 part (4) and the optional evidence in T1.102-T1.108 read `[TRAITDIAG]` lines that CMC 1.68.30 on the dev install still prints until the next CMC deploy and no later build prints; each of those rows carries a dated 2026-09-17 note saying so.

### 2026-09-27 - Content_Update_Plan (1.14.0, The Physic Garden)
- **Verdict:** 8 of 8 features built, 0 plan rows unbuilt. Features 1-7 shipped in HerbsAndFungi 1.14.0 (`4c274ef4c`, embedded in MUM 2.1.63, nothing uploaded to Nexus); feature 8 (seasons at the H&F forage locations) built in the commit that archives this plan and folded into the same unpublished 1.14.0 rather than bumped. Section 9: steps 1-4, 6 and 7 done; step 5 (suite repack and deploy) is a release step the owner triggers, so feature 8 is on no install yet. 9 items awaiting verification, all with tracker rows.
- **Disposition:** ARCHIVE to `Documentation/Design/HerbsAndFungi_Content_Update_1_14_0_As_Built.md` with an archive banner; `Documentation/Plans/HerbsAndFungi/` is now empty. Inbound citations repointed in the same commit: two test headers (`StatBase-ImmuneNoBaseWrite`, `HerbsAndFungi-ForageSeasons`), three scenario `refs` (T2.288-T2.290) and the plan's own tracker rows. `Documentation/Ideas/HerbsAndFungi/IDEAS.md` reconciled: the 1.14.0 rows moved to Shipped, and the vanilla-covered rows the 2026-09-26 note already ruled out moved from Medium-Term into "Ruled out" with their reasons.
- **Pruned:** nothing from the plan text; it left `Documentation/Plans/` whole. From IDEAS.md: 10 open-looking rows (spent-log sink, peanut chain, tisane crop, the Death Cap Pain measurement, seasonal forage, smoked variants, herbal soap, drying shed, compost bin, vanilla-flavour FlavourMatrix pairs) moved to Shipped or Ruled out, none deleted.
- **Evidence:** feature 8: `DropInjections.json` carries `Seasons` on 35 drops (Chaga and Morel none, by design), matched to `GameLoadPatch.cs`'s `AddMushroomDropToAction` season arguments by the new `HerbsAndFungi-ForageSeasons.Tests.ps1`, watched red with 35 violations on the pre-feature file and green after. Features 1-7 re-read against JSON on 2026-09-27: 7 brew CIs, none with `StatModifications` on the player (feature 1; the Drink actions do carry them, so the probe can hit); `HF_HerbalTonic.json` range 0-96 at -1 a tick, statuses +25 (1-48) and +50 (49-96) ImmuneSystem, `StatBase-ImmuneNoBaseWrite` green (feature 2); `DeathCapTincture.json` Drink writes 3 stats, none Pain `ec5f330267076884dbe9cfdd2fd8503b` (feature 3); `ChagaTea.json` Drink +48 Herbal Tonic, Chaga drops at hfLocAlderWoods 6.0 and three other H&F locations 2.0 plus the vanilla birch rule (feature 4); `GinsengPlantGrowing`/`YarrowPlantGrowing`/`PeanutPlantGrowing` Progress 1344/480/960 at rate 1 (14/5/10 days), harvests (3,4)/(3,4)/(3,5), hoe `DontSpend` with `UsageChange -1` (features 5-6); all six `MushroomLog*Mature.json` Harvests drop 20 mushrooms, WoodShavings `da61c004e1fc3a44b909a56ddded9189` (2,3) and Firewood `758be01db25eb4649a288d64b7600b9b` (1,1) (feature 7). Gates on the archiving tree: 12 Pester files, 147 passed 0 failed (localization, parity, stat-base, flavour, drop schema, csproj includes, citations, forage seasons); `Check-LocalizationParity.ps1` CLEAN 1518/1518; Release build 0 warnings 0 errors; `/audit-mod` preflight 0 CRITICAL, 1 WARNING (J27, the known false positive), score 10/10. The live dev-install boot log reads `DropInjector: 190 drop(s) appended, 1 season-gated` (the 1 is MasterTesting's), which is the pre-feature-8 state T2.296 is built to go red on.
- **Verification debt:** T2.288 (teas dose once), T2.289 (Herbal Tonic), T2.290 (tincture without Pain), T2.291 (Chaga chain), T2.292 (Chaga in Winter, MANUAL), T2.293 (Ginseng and Yarrow), T2.294 (peanuts), T2.295 (log wood return), T2.296 (H&F-location seasons; also satisfies the framework's T2.364), all present in `.claude/playthrough-test-status.json`, all `pending`. T2.292 and T2.296 need feature 8 deployed first.

### 2026-10-03 - MASTER_PLAN (round 2 after 1.14.1, `/audit-plan` pass)
- **Verdict:** of the plan's 67 features, 5 verified done and pruned (2, 4, 6, 7, 10), 4 built and awaiting a person (3, 8, 12, 67), 9 already closed to the Considered table (21, 38, 39, 45, 46, 48, 49, 50, 52), 0 failed, 49 open (2 of them blocked outside the plan: 5 on framework A1 `AgentUids`, 65 on PartnerOverhaul W0.5 and W0.7). Pack: 53 open before, 51 after (Prompt 13, the stew and spice harness probes, VERIFIED DONE; Prompt 82, feature 67, BUILT and moved to awaiting); awaiting list 7 before, 4 after. Nine plan or pack claims were STALE and corrected with dated notes, the largest being feature 1's container gate (see Evidence).
- **Disposition:** KEEP IN PLACE. Forty-nine features of real code work remain, so the plan and its pack stay in `Documentation/Plans/HerbsAndFungi/`; the mod is not feature complete and does not move to `Stable/`.
- **Pruned:** plan sections 2, 4, 6, 7 and 10 (deleted; their Selected-table rows, the Wave 0 outcome bullets for 10 and 12, and open questions 4, 6, 8, 9, 16 and 34 went with them); sections 3, 8, 12 and 67 collapsed to awaiting-verification pointers; four resolved Risk-register rows removed. Pack: Prompts 13 and 82 removed, the awaiting list rebuilt. No numbering was closed up.
- **Evidence:** the tracker (`.claude/playthrough-test-status.json`) holds T2.1000, T2.1001, T2.1002 and T2.1005 as `pass` (harness run `walkthrough-20261003-074941`, H&F 1.14.1 at `eb44276cd`), T2.1003, T2.1004 and T2.1006 `pending` with a recorded harness PASS and a person's leg left, T2.1007 `pending` and not yet run. Code re-read 2026-10-03: `CardData/Location/WildBeesNest.json` and four `herbs_fungi_wild_bees_nest` entries in `WorldMap/MapNodes.json`; `RipePuffball.json` and `Puffball.json` transforming into it; the Herbalism stat GUID `85559650c938ef843af92c18f5b0c6c7` on nine medicine blueprints and on the Mature cards of Ginseng, Yarrow, Chamomile, Plantain, Dandelion and six mushroom logs; four birch rules in `DropInjections.json` (17, 12, 13 and 16 locations); README, PLAYER_GUIDE and CHANGELOG carry the truffle (oak and alder), Death Cap (Apply Poison) and lamp (`tag_Oil`) passages feature 4 promised. A UID probe over 37 names of open features across `CardData`, `CharacterPerk`, `GameStat`, `SpiceTag`, `WorldMap`, `DropInjections.json`, `BlueprintTabs.json` and `Patcher` returned 0 for 36 of them (the 37th, a bare `paper`, matched three flavour-text lines on the nest and the puffballs, while `hemp_paper` and `botanical_field_guide` returned 0), against controls `wild_bees_nest` 2, `ripe_puffball` 2, `plantain_plant` 4, `mushroom_pasty` 11, `herbal_tonic` 18; no `GameSourceModify/`, `SelfTriggeredAction/` or `EncounterGuards/` folder exists. Prompt 13: `Documentation/Research/HerbsAndFungi/harness_stew_spice.md` (`a279a25a`) measured that no live vanilla vessel carries `tag_StewContainer` (CookingPot, ClayCauldron and ClayBowl read false; they carry `tag_CookingContainer`), so feature 1's Add to Stew gate must change, that route A holds, and that the framework fills a mod SpiceTag's `Stat`. `CSFFModFramework/Injection/DropInjector.cs` documents `SeasonChance` (2.28.0) and `Api/BlueprintAlternates.cs` holds `AddAlternateIngredientIn`, so features 28 and 37 are unblocked; `AgentUids` returns 0 hits in `CSFFModFramework/` and no `tag_x_AnimalRemedy` or `tag_x_Bathtub` exists in Sirus23 or ACT JSON. Not run: any Pester gate or build (no PowerShell in this session), so gate colours were not re-measured.
- **Verification debt:** T2.1003 (forage at a vanilla birch tile in three seasons), T2.1004 (the three blueprints in Farming / Agriculture and the six pictures), T2.1006 (the four pasty pictures) and T2.1007 (the whole Wild Bees' Nest), all present in `.claude/playthrough-test-status.json` and `pending`. The older 1.14.0 rows (T2.288 to T2.296, T2.171, T2.189, T2.209, T2.230, T2.231, T4.41) are unchanged by this pass.

### 2026-10-04 - MASTER_PLAN (`/audit-plan` pass 2, against HEAD `808fa1458`)
- **Verdict:** counts: 45 open pack prompts checked one by one, 0 verified done, 1 built and awaiting a scenario run (Prompt 18, Chanterelle route A in `af0d49de0`; it has no tracker row yet), 44 open (Prompt 27 newly unblocked, Prompt 79 blocked on PartnerOverhaul W0.5 only, Prompt 78 and Prompt 39 buildable with their harness legs waiting on Sirus23 feature 39 and ACT's `tag_x_Bathtub`). The 10 awaiting-verification entries (features 3, 8, 12, 67, 11, 16, 27, 30, 42, 24) were each re-read in source and are still built and unconfirmed. Nothing landed since the 2026-10-03 pass beyond what the pack already records: no file under `HerbsAndFungi/` outside `bin/` has changed since `28334ea0a`.
- **Disposition:** KEEP IN PLACE. 44 prompts of real code work remain, so the plan and its pack stay in `Documentation/Plans/HerbsAndFungi/`; the mod is not feature complete and does not move to `Stable/`.
- **Pruned:** no pack prompt (45 before, 45 after). Plan: the "design as written" bodies of sections 16, 24, 27, 30 and 42 (and the trailing "design as written" sentence of 11, 27, 30 and 42) are deleted, leaving each heading and its awaiting-verification pointer (their facts are in the pointer, "Wave 0 outcomes" and Prompt 70); no section was renumbered. Corrected as STALE-CLAIM, with dated notes: feature 5 and Prompt 27 called framework A1's `AgentUids` absent, but it ships in CSFFModFramework 2.28.0 (`071ab27e6`), so Prompt 27 lost its dependency and moved up beside the ready builds, ahead of Prompt 19; feature 65 and Prompt 79 now name W0.5 as the only open PartnerOverhaul answer (W0.7 was answered 2026-10-04). Pack header restamped to `808fa1458`.
- **Evidence:** a name probe over `HerbsAndFungi/` (318 files, `bin/`, `obj/`, `lib/`, `.audit/` excluded) for about 90 tokens drawn from the open prompts' UIDs and card names returned 0 file-name hits and 0 text hits for every open feature's own UID (linseed_cake, maggot, pine_nut_oil, spore_print, grow_bag, herbal_ferment, mossbed, spiced_pickle_brine, berry_pulp, berry_leather, honeyed, watercress, OldGrowthGrove, grove_open, herb_butter, herb_cheese, fungal_bloom, velvet_shank, spice_tonic, bath_soak, soothing_soak, mulled, needle_ale, unknown_chanterelle, false_chanterelle, academy_herbalism, hotspot, toxin_tolerance, PartnerDuty and every pool item in Prompts 67 to 76), against controls `gruit` (4 file names, 11 text hits), `herbal_tonic` (20), `wild_bees_nest` (4) and `honey_syrup` (4) from features already built. The nonzero hits were each read and none is a build: `wax` is two existing Beeswax mentions, `tofu` is flavour text on `CookedPuffball`, `hops` and `oats` are gruit text and one CHANGELOG line, `DeathCapWard` is the 13 orphan localization rows only, `AgentUids`, `SeasonChance`, `AddAlternateIngredientIn` and `AnimalRemedy` appear only in this ROADMAP, and `tag_Flower` sits on `DryingTray`, `FlowerGarland`, `HempFlower` and `IncenseBundle` but on none of `Chamomile`, `Dandelion` and `WildFlowers` (Prompt 77). Structural checks: `GameLoadPatch.cs` still holds 48 `AddMushroomDropToAction` references (Prompt 45); `DropInjections.json` holds 0 `"Winter"` rows (Prompt 23); `MapNodes.json` `StripLegacyBoardUIDs` still lists GUIDs on five nodes (2, 3, 4, 1 and 2 entries: the plan's four tree nodes plus Misty Falls' waterfall path; Prompt 81); no `EncounterGuards/`, `SelfTriggeredAction/` or `harness_runtime_names.md` exists (Prompts 27, 56, 58, 14); 62 files still carry "Add to Stew" and only `Chanterelle.json` triggers on the two hot-water GUIDs with `tag_CookingContainer`, WorksInverted, ModType 1 (Prompt 18 built, Prompt 19 open), with `HerbsAndFungi-StewCI.Tests.ps1` holding the pins. Source reads for the 10 awaiting entries: `Bp_HoneySyrup_Chamomile`, `Bp_HoneySyrup_Plantain`, `HoneySyrup`, `Bp_GruitBundle`, `GruitBundle`, `GruitAle`, `Spice_Gruit`, `Bp_ForagerSeasoning`, `ForagerSeasoning`, `Spice_ForagerSeasoning`, `Bp_HempPulp`, `HempPulp`, `HempPulpSoaked`, `Bp_Paper`, `Paper`, `Bp_BotanicalFieldGuide`, `BotanicalFieldGuide`, `GameSourceModify/ClayCauldron.json` (carries `tag_x_MedicineBrewStation`, one `GameSourceModify` csproj include), `WildBeesNest` (listed in `MapNodes.json`) and the +8 FoodPoisoning GUID in `MorelMushroom` and `ChickenOfWoods` all exist. Framework: `CSFFModFramework/Loading/EncounterGuardLoader.cs` reads `AgentUids` (`ReachesAgentAttacks`), `Patching/AgentEncounterGuardPatch.cs` exists, `Injection/DropInjector.cs` documents `SeasonChance` and `Api/BlueprintAlternates.cs` holds `AddAlternateIngredientIn` (Prompts 44 and 51 remain unblocked at source); no `tag_x_AnimalRemedy` or `tag_x_Bathtub` exists in Sirus23 or ACT JSON (true on 2026-10-04; corrected 2026-10-09: ACT `fc4f45414`, 2026-10-05, ships `tag_x_Bathtub` on `CopperBathtub_Full_Placed.json` and `CopperBathtub_Warm_Placed.json` in ACT 1.18.0, so the tub half of the Bath Soak no longer waits on ACT). Tracker: all 29 T-ids in the plan and all 11 in the pack resolve in `.claude/playthrough-test-status.json`. Not run: any build, game or harness scenario; `Plans-Policy.Tests.ps1` (12 passed) and `PlaythroughRow-Citations.Tests.ps1` (13 passed) were run after the last doc edit.
- **Verification debt:** T2.1003, T2.1004, T2.1006, T2.1007, T2.1008, T2.1009, T2.1010, T2.1011, T2.1012 and T2.1013 (all `pending`; T2.1013 has nothing for a person), plus ACT's T2.771 for the Copper Cauldron half of feature 24. Prompt 18's pilot has no row: it needs one filed for the 1 of 62 Add to Stew cards converted, naming `T2.1014_hf_stew_chanterelle_pilot_absorbs.json`.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** counts against HEAD `189f8b99e`: 2 features verified done and pruned (3 and 12, T2.1003 and T2.1006 confirmed 2026-10-07 by harness and AI judge), 0 obsolete beyond nine already-closed features whose leftover Selected rows were removed (21, 38, 39, 45, 46, 48, 49, 50, 52), 11 features built and awaiting verification (1, 5, 8, 11, 15, 16, 18, 23, 30, 42, 67; 12 pack bullets, feature 1 having two), 1 failed (17: its harness scenario failed step 8 twice on 2026-10-06, Collect Maggots leaving the Rotten Remains), 5 stale claims corrected (sections 11, 16, 23's pot half, 42 and 67 said "not yet run live" while harness PASS reports sit unrecorded), and 39 open pack prompts (38 carried, plus Prompt 83 for feature 17's fix).
- **Disposition:** KEEP IN PLACE - 39 open prompts of code work remain (37 open features, 9 to 66, plus feature 17's fix), so the plan and its pack stay in `Documentation/Plans/HerbsAndFungi/` and the mod does not move to `Stable/`.
- **Pruned:** plan sections 3 and 12 (deleted, with their Selected rows, Next-work rows, open question 5 and the birch risk row; feature 3's rule layout folded into section 29's deps line and feature 12's pasty route into section 59's Shape note, and pack Prompt 73 repointed there); section 1 collapsed to a BUILT line keeping the route-A CI shape features 9, 13 and 23 build on; the nine closed features' Selected rows; Wave 0 rows E6, E13, E19, E20, E21, E22, E25, E26, E27 and E28 (answered or closed); Wave 0 outcome bullets 15 and 23; open question 1; three resolved risk rows (stew gate tag, wort spice, wound infection); the plan's dated status history, condensed to one block whose text resolves at `189f8b99e`. Pack: the feature 17 awaiting bullet (re-opened as Prompt 83). Plan 1737 to 1606 lines; pack 539 to 556 lines (the fix prompt added).
- **Evidence:** `git log bd36ba798..HEAD -- HerbsAndFungi/` (bin and obj excluded) shows one build commit, `95b7669af`, and `git status` shows no uncommitted H&F source. A JSON read of every `HerbsAndFungi/CardData` card found 62 "Add to Stew" CIs, all 62 WorksInverted on `LQ_Water` and `OLD_LQ_StewWater`, gated on `tag_CookingContainer`, `GivenCardChanges` ModType 1 with `AddFlavoursFromOtherCard` and `ScaleChangesPerPortionOfLiquid`, `ReceivingCardChanges` ModType 3, and 0 files still naming `tag_StewContainer`. A probe of 33 open-feature UIDs and tokens (spore_print, grow_bag, herbal_ferment, mossbed, velvet_shank, watercress, unknown_chanterelle, fungal_bloom, tag_x_AnimalRemedy, the PartnerDuty_Eat GUID and others) over `CardData`, `GameStat`, `SpiceTag`, `WorldMap`, `Patcher`, `DropInjections.json` and `BlueprintTabs.json` found none of those features (one "identify" hit is a code comment), while four built-feature controls (wild_bees_nest, honey_syrup, maggots, linseed_cake) each hit; no `SelfTriggeredAction/` folder exists. The tracker holds T2.1003 and T2.1006 in `confirmedItems`; the three T2.1019 reports under `TestHarness/client/reports/herbsandfungi-maggots-*` read FAIL (2026-10-05: no such action, a stale deploy; 2026-10-06 09:39 and 19:25: `card.destroyed` seen 0 times, the remains still on the board in `step08-fail.jpg`), and `autoplay-audit-20261007-081615.json` lists T2.1019 as "latest verdict FAIL". Harness PASS reports exist unrecorded for T2.1007, T2.1008, T2.1009, T2.1011 and T2.1015 (two runs each on 2026-10-06 for the pot half). Sirus23 still carries no `tag_x_AnimalRemedy` (grep, 0 files). No tracker row was filed: every built item already has one.
- **Verification debt:** T2.1004, T2.1007, T2.1008, T2.1009, T2.1011, T2.1015, T2.1017, T2.1019 (failed in harness; Prompt 83), T2.1021, T2.1022, T2.1023, T2.1024, T2.1025 and T2.1026, all `pending` in `.claude/playthrough-test-status.json`.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** counts against HEAD `51641ef64`: 1 feature verified done and pruned (30, Forager's Seasoning: T2.1010 and T2.1026 both in `confirmedItems`), 0 obsolete features, 1 pack prompt obsolete (Prompt 83, feature 17's fix: the cause was the framework, not H&F), 10 features built and awaiting verification (1's pilot, 5, 8, 11, 15, 16, 17, 18, 23's pot half, 42 and 67: 11 pack bullets), 1 failed (feature 1's 61-card sweep, T2.1021's scenario FAIL on all five runs 2026-10-07 to 2026-10-09, re-opened as Prompt 84), 11 stale claims corrected (the collapsed BUILT lines of sections 1, 5, 8, 11, 15, 16, 17, 18, 23, 42 and 67 read 'not yet run' or 'not recorded' for rows since confirmed, or named a person half the 2026-10-09 lane split moved to a new row), and 39 open pack prompts (38 carried plus Prompt 84).
- **Disposition:** KEEP IN PLACE - 39 open prompts of code work remain (36 open features, 9 to 66, the art batch, the plan close and feature 1's sweep fix), so the plan and its pack stay in `Documentation/Plans/HerbsAndFungi/` and the mod does not move to `Stable/`.
- **Pruned:** plan section 30 (deleted with its Selected row, its Next-work row and open question 24; its SpiceTag 'Add' shape folded into pool 53's Mustard row as the `ForagerSeasoning.json` and `Spice_ForagerSeasoning.json` file names), section 17's FAILED line (replaced by a BUILT pointer that records the framework cause, `e57d44cfc`), and the Next-work table, failed paragraph and order list (rewritten for 2026-10-09). Pack: Prompt 83 (removed; `CSFFModFramework/Data/WarpResolver.cs` gained an enum branch for WarpType 4/6 appends in `e57d44cfc`, framework CHANGELOG [2.28.0]), the feature 30 awaiting bullet, and the stale deps of Prompts 23 and 28 (they waited on T2.1021; they copy the pilot's confirmed shape). Added: Prompt 84 (feature 1 sweep diagnosis) and a one-line Failed list.
- **Evidence:** `git log 189f8b99e..HEAD -- HerbsAndFungi/ ':!HerbsAndFungi/bin' ':!HerbsAndFungi/obj'` returned 1 commit (`56ef680ed`, `ROADMAP.md` only), against the control without the exclusions, 2 commits (`e57d44cfc` adds `bin/Release` copies), so no open feature landed; `git status` shows 0 uncommitted H&F source files and `git worktree list` 1 worktree; `HerbsAndFungi/SelfTriggeredAction/` is absent and `DropInjections.json` holds 0 `Winter` rows (Prompts 56, 58, 23 still open). Tracker read: of the plan's H&F rows, T2.1004, T2.1008, T2.1010, T2.1014, T2.1016, T2.1018, T2.1020, T2.1022, T2.1023, T2.1025 and T2.1026 are in `confirmedItems`; T2.1007, T2.1009, T2.1011, T2.1015, T2.1017, T2.1019, T2.1021, T2.1024 and person rows T2.1357 to T2.1367 are pending. Harness reports under `TestHarness/client/reports/`: T2.1019 FAIL on five runs 2026-10-05 to 2026-10-08 and PASS at 2026-10-09 03:32 and 08:04 (step 10 logs `card.destroyed` for the Rotten Remains); T2.1021 FAIL on all five runs (Truffle Butter 'does nothing' in all four runs that reached it, Ginseng in two, King Oyster Powder in three, all six legs on 2026-10-09); PASS unrecorded on 2026-10-09 for T2.1009, T2.1011, T2.1015, T2.1017 and T2.1024, and on 2026-10-06 for T2.1007, whose 2026-10-09 rerun stopped at step 108 when the character froze to death. A static diff of the six T2.1021 cards' Add to Stew CIs (WorksInverted, container tag, RequiredGivenDurabilities, DaytimeCost) found them identical to Chanterelle's at top level, so the cause is not settled here. Sirus23 JSON holds 0 `tag_x_AnimalRemedy` files (Prompt 78's harness leg still blocked) and the PartnerOverhaul plan still lists W0.5 unreported (Prompt 79 blocked). Not run: any build, deploy, game or harness scenario.
- **Verification debt:** T2.1007, T2.1009, T2.1011, T2.1015, T2.1017, T2.1019, T2.1021 (failed in harness; Prompt 84), T2.1024, T2.1357, T2.1358, T2.1359, T2.1360, T2.1361, T2.1362, T2.1363, T2.1364, T2.1365, T2.1366 and T2.1367, all `pending` in `.claude/playthrough-test-status.json`.

### 2026-10-09 (later) - MASTER_PLAN, second /cleanup-plans pass
- **Verdict:** NOT drained. 39 open prompts, 11 awaiting verification, 1 failed (feature 1 sweep, Prompt 84). Nothing newly built or obsolete; 3 stale claims corrected.
- **Disposition:** KEEP IN PLACE - code work remains.
- **Pruned:** none. Corrected: T2.1004 (feature 8) was un-recorded at 3b363b0d6 (rig framework swap) and needs a rerun; the T2.1011 (feature 42) PASS was rejected by the skeptic read, scenario reworked in 618ef7c78; T2.1007 (feature 67) scenario reworked, needs a rerun; the T2.1021 FLAKY triage folded into Prompt 84.
- **Evidence:** audited against 1d9cbf121. H&F source commits since 51641ef64: c21bc3bf7 (Drying Stack research Quantity, outside the plan) and e762ec6f4 (bath-soak README). None of 23 open-feature tokens appears in H&F source, while six built-feature controls do and 62 cards carry Add to Stew. Gates 30/0.
- **Verification debt:** T2.1004, T2.1007, T2.1011, T2.1021 (reruns); T2.1009, T2.1015, T2.1019, T2.1024 (PASS, unrecorded); T2.1017 recorded CONFIRMED by the 2026-10-09 second review. A tracker row for c21bc3bf7's Drying Stack gate is owed.
