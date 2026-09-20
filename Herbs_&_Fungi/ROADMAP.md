# Roadmap: Herbs and Fungi
Version at time of writing: 1.10.18
Date: 2026-09-05
Audit score: 10/10 - PASS (0 CRITICAL, 0 DESIGN GAP, 8 WARNING, 4 MINOR) - see [.audit/summary.md](.audit/summary.md)

## Current State

**Theme**: A forage-and-preserve survival mod. Gather mushrooms, berries and medicinal herbs; grow
hemp and cultivate mushroom logs; dry, grind, press, ferment and brew them into food, oils, teas and
remedies. Early-to-mid-game gathering and food/medicine production with a light late-game
cultivation and WorldMap-exploration layer. It is the repo's gold-standard DATA mod and the
architecture other content mods are told to copy.

**Content**: 109 items + 16 liquids / 45 blueprints / 29 structures / 15 perks / 98 custom PNGs /
2 GameStats / 6 WorldMap clone nodes / 0 GIFs / 0 SelfTriggeredActions / 0 spawn Triggers.
Declarative files shipped: `BlueprintTabs.json`, `DropInjections.json`, `MapMod.json`,
`Features.json`. C# surface: 6 files, ~1,735 LOC, 4 patch classes.

**Stability**: 10/10 - PASS. Zero CRITICALs, zero design gaps. The one long-standing CRITICAL (the
PickleVat / OpenPickleJar flat-vs-nested `DroppedOnDestroy` schema bug, 9 files) was closed in
v1.10.18 and re-verified against disk during the 2026-09-05 consolidation: all 9 files nested,
mod-wide flat-drop scan returns 0, `bin/Release` byte-identical on 199/199 CardData files. Build is
green (0 errors, 0 warnings); versions are synchronized at 1.10.18 across ModInfo.json / Plugin.cs /
README.md; localization is 1401 EN rows over 1240 JSON keys with **Chinese parity CLEAN at
1283/1283**; acquisition coverage is fully closed (0 unreachable, 0 dead-end).

**Open work**: **No open or pending retrospectives are specific to this mod.** The single H&F-tagged
retro row (`sealablegates-portal-hardening-2026-07-02`) is Graduated, confirmed in-game 2026-07-23.
The real outstanding work is *verification debt*, not defects: three shipped-but-unconfirmed fixes
(pickle vat/jar return, Hemp Butter activation chain, seasonal forage gating) and two missing
playthrough-tracker rows. That is Phase 0 below.

**Framework compliance**: **Tier 2 - current.** Extends `ContentModPlugin` (the base emits the one
canonical startup Info line and owns Harmony/UnpatchSelf); pickle-vat routing goes through
`Api.ActionRouter`; the Apothecary quest-gate uses `SpawnService.CardSpawned`; blueprint-tab and perk
injection are fully declarative (`BlueprintTabs.json` + framework `PerkInjector`); forage injection
is partly declarative via `DropInjections.json`. **No deprecated patterns present** - grep confirms
no `DropCollectionGuardPatch`, no `InitializeNullFields`, no `ModLoaderVerison`/`ModEditorVersion`,
no unfiltered hot-path prefixes, no local duplication of framework services. Code quality scores
10/10 on the reliability axis.

---

## Phase 0: Stabilize

> Audit score is 10/10 and there are no open retrospectives, so this phase would normally be skipped.
> It is retained for one reason: **three player-visible fixes shipped in v1.10.17 and v1.10.18 have
> never been confirmed in-game, and two of them have no tracker row at all.** A fix nobody has
> watched work is not a fix. No new content should land until these are gated.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **Backfill the playthrough tracker.** `code-quality.md` cites row `T2.171` as the gate for the seasonal-suppression assumption; the tracker's highest T2 id is T2.170 - the row does not exist. Add it, plus a row for the v1.10.18 `OpenPickleJar` "Return Bowl" fix. Do this FIRST: no agent can advance a human check, and the plan doc is currently the only place these acceptance criteria are written down | Tracker integrity | P0 | Quick |
| **Correct row T2.118's note.** `hf_pickle_vat_dropped_on_destroy_recovery` still reads "SUSPECTED BROKEN AS SHIPPED" for a bug that is now fixed in code. A tester reading it will mis-triage a pass as a surprise | Tracker integrity | P0 | Quick |
| **Verify the pickle vat/jar return in-game** (covers C1 end-to-end). Eat 5 servings from any Ready vat: an empty Pickling Vat AND an Open Pickle Jar should appear; "Return Bowl" on the jar should then yield a ClayBowl. This also settles the open `DropOnDestroyList: false` question - the field gates whether `DroppedOnDestroy` fires at all (`.decomp/GameManager.cs:10521`), and the mod deliberately matches vanilla's `ImprovisedShelter`/`Hideout`/`RainCatcher` shape rather than diverging on an unverified guess | Verification | P0 | Medium (needs a play session) |
| **Verify the Hemp Butter activation chain** (T2.153, pending). Mix Hemp Flower Powder with a fat -> Inactive; heat in a container to 40+ until Activation Progress 6/6 -> Active; rest ~1 in-game day -> the dosed block with its 3 dose DAs | Verification | P0 | Medium |
| **Verify seasonal forage gating across a season boundary** and confirm the one unproven engine assumption: that `SeasonCounter_X` reads below 1 outside season X. Failure mode is benign (mis-timed drops, no crash) and the fix is a `InputValueRange` tweak - but it is currently unproven and shipping | Verification | P0 | Medium |
| **Verify T2.117** (pickle vat "Uncap" confirmation prompt, pending since 1.10.14) - cheap to fold into the same session as the vat check above | Verification | P1 | Quick |

---

## Phase 1: Foundation

> Table-stakes hygiene. Nothing here is broken; these keep the mod from drifting.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **Regenerate `.audit/feature-map.md`.** Dated 2026-09-01 for v1.10.16, it still grades Pickling System `BROKEN (open CRITICAL)` and Hemp Farming `MEDIUM (Hemp Butter sub-chain BROKEN)`, and still asserts "6 of 10 teas have no in-run craft path" - all three are wrong as of today (the 6 herb teas all brew via "Mix into Hot Water" CIs; both BROKEN root causes are fixed). Per CLAUDE.md Audit-Artifact Resolution Discipline this is precisely the artifact that gets laundered into re-promoted work | Artifact hygiene | P1 | Quick (`/feature-map HerbsAndFungi`) |
| **Retire `.audit/fix-plan.md`.** Dated 2026-06-20; its Fix 2 argues at length for stripping the season claims and NOT implementing seasonal gating - the exact opposite of what shipped in v1.10.18. Leaving it in `.audit/` is a live re-promotion hazard | Artifact hygiene | P1 | Quick |
| **Regenerate `lib/Assembly-CSharp-nstrip.dll`.** The plain `Assembly-CSharp.dll` is current (2026-09-03, md5-identical to the framework canonical) but the nstrip variant the `.csproj:33` actually references is dated 2026-06-27. It cannot be fixed by copy - it must be re-run through NStrip on the current game binary (needs the user's external NStrip install). Tracked fleet-wide as row T1.71 | Game-update hygiene | P1 | Medium (external tool) |
| **Re-run the five stale sub-audits** - `/audit-items`, `/audit-blueprints`, `/audit-perks`, `/audit-images`, `/karpathy-check`. All predate the 2026-09-02 source commit (items 2026-06-14, blueprints and perks 2026-05-20, images 2026-06-22, karpathy 2026-07-20) and are UNTRUSTED input. Low urgency - none of their findings are load-bearing today - but they should not stay this far behind on the repo's reference mod | Audit freshness | P2 | Medium |
| **Harden biome bucketing.** Forage injection buckets locations by `LocalizationKey.Contains()`, which already required a hand-written `ClearingOak/Alder/Pine` de-collision after it silently double-stacked drops on three biomes. Replace the open-ended `Contains` chain with a curated exact-key/UID table - the pattern the mod already uses for its own clone envs at `GameLoadPatch.cs:439-446`. Shipped behavior is correct today; this removes a recurring per-game-update hazard | Maintainability | P2 | Medium |

---

## Phase 2: Core Expansion

> The three most impactful additions that extend the existing loop. All verified absent from disk on
> 2026-09-05; full backlog in [.audit/ideas.md](.audit/ideas.md) and
> `Documentation/Ideas/HerbsAndFungi/IDEAS.md`.

### Culinary mushroom seasoning powders
**What**: Grind dried Black Trumpet, Shiitake and King Oyster into umami seasoning powders. Clone the
`GinsengGround` grind-CI; give each Savoury/Earthy `FlavourTags`.
**Why**: The mod ships four *medicinal* grounds (Ginseng, Reishi, Lion's Mane, Yarrow) and zero
culinary ones, so its three best gourmet mushrooms dead-end at "cooked" or "dried." The powders feed
the vanilla Pouch flavour-transfer system the mod is already wired into, so they gain reach for free.
**Requires**: none (pure JSON/CSV/sprite).
**Complexity**: Quick

### Truffle Butter + Truffle Salt
**What**: Truffle + fat -> Truffle Butter (clone the HempButter/PeanutButter state-and-grind pattern);
Dried Truffle + Salt -> Truffle Salt via a Grind CI. Both with strong Earthy+Savoury `FlavourTags`.
**Why**: Truffle is the mod's luxury item (trades 50-120, dug from under old-growth oak) and it
dead-ends at Cooked Truffle plus Truffle Oil. The butter chain it would clone is now confirmed
working end-to-end after the v1.10.17 Hemp Butter OnZero fix, so the pattern is proven rather than
theoretical.
**Requires**: Phase 0's Hemp Butter verification (it validates the pattern being cloned).
**Complexity**: Medium

### Mushroom Broth buff variants
**What**: 2-3 sibling broths beside `Bp_MushroomBroth`, each gated on a specific dried mushroom and
granting a themed buff via `StatModifications` on the drink DA - Reishi (immune/stress), Lion's Mane
(cognition), Chanterelle (morale).
**Why**: Base Mushroom Broth shipped but has no variants, while the tea line already demonstrates the
exact buff-on-drink pattern nine times over. This is the concrete JSON-only slice of
`Documentation/Ideas/HerbsAndFungi/AdvancedCooking.md` - no station, no C#, no design decisions left
open.
**Requires**: none.
**Complexity**: Quick

---

## Phase 3: Integration & Depth

### Herbalism head-start perk
**What**: A Situational-tab perk biasing `Skill_Herbalism` (`85559650c938ef843af92c18f5b0c6c7`) via
`StartingStatModifiers`, built to `CSFF_Patterns.md` Skill Head-Start Perk.
**Why**: **Ground-truthed gap - all 15 shipped perks have 0 `StartingStatModifiers` and 0
`PassiveStatModifiers`; every one grants items.** The mod already awards Herbalism XP against that
GUID on five dried items, so the skill is meaningfully in play but the character-creation offer has
no way to express it. Cheap, high pick-rate, and diversifies a perk list that currently offers 15
variations of "a bag of things."
**Requires**: none.
**Complexity**: Quick

### Oil as a cross-mod material (CMC / ACT / WDI)
**What**: Expose the H&F oil family (`HerbalOil_*`, HempSeedOil, PeanutOil, and a new Linseed Oil) as
lamp fuel via `tag_Oil` for CMC's CeramicLamp and ACT's lantern refuel CIs, and as a wood-finishing
material for WDI's wooden water structures and ACT's wooden tools.
**Why**: The Oil Press is one of only four VERIFIED features in the mod and produces eight oils whose
only current consumers are inside H&F itself. Lamp fuel and wood treatment are both constant
mid-game needs that no mod currently serves. Linseed Oil additionally gives the Seed Bag perk's Flax
seeds their first H&F use.
**Requires**: coordination on tag ownership with CMC/ACT/WDI; check first whether
`Bp_OilPressSeeds` already accepts flax generically, in which case Linseed collapses to adding a
named output.
**Complexity**: Medium

### Dried herbs into WDI's Grinding Mill
**What**: A WDI-side operation blueprint milling `tag_DryingRackSanctuary` dried herbs in bulk.
**Why**: Grinding is the mod's most repeated manual action; WDI already owns the bulk-milling
station. Entirely WDI-side - zero H&F change, which makes it the cheapest cross-mod win available.
**Requires**: WDI coordination only.
**Complexity**: Quick (WDI-side)

### Spore Print propagation
**What**: A DA on mature mushroom logs yielding a Spore Print; inoculation consumes 1 print instead
of 10 matching mushrooms.
**Why**: Mushroom-log cultivation is currently one-way - a spent log has no downstream use and
re-inoculation costs another 10 mushrooms, which caps how much the system is worth engaging with.
Prints close the loop and make cultivation genuinely renewable.
**Requires**: an open design decision (does the print replace or parallel the mushroom recipe? are
prints species-specific?). Resolve that before building.
**Complexity**: Complex

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Card art for the medicinal liquids | All 16 share one generic clay-bowl/thirst icon. Start with the flagship teas - Ginseng, Reishi, Lion's Mane, Sleep | Medium |
| Card art for the pressed oils | Six oil items sit on the **unverified** vanilla sprite name `Bowl_Clay`; `HempStalks` on `Nettle_Stems`. A wrong vanilla name renders a blank card silently (CLAUDE.md Sprites/Images), so this closes a real risk, not just a look | Medium |
| First GIF animation | The mod ships 98 PNGs and 0 GIFs. The Oil Press (active pressing) or a Growing Fungal Log (colonising) is the natural first candidate - see `Documentation/CSFF_GIF_Authoring.md` | Medium |
| Soften the fleet preflight M6 check | "CookingRecipes on a CT0 item" fires on `DryingTray.json` every run and is a verified false positive (`CardData.CanCook` is CardType-independent; 50 vanilla CT0 items ship CookingRecipes). It costs a re-adjudication on every audit of this mod | Quick (fleet-side) |
| Extend the season lever to content | v1.10.18 built `ApplySeasonSuppression` and uses it only on forage drop chance. Seasonal mushroom flushes or an autumn-only bracket fungus are now data edits rather than new code | Quick, per item |

---

## Long-term Vision

At v1.11-1.12 Herbs and Fungi should be the mod that makes a CSFF forest worth walking through in
every season: forage that changes with the calendar (the machinery for which now exists and needs
only content), a preservation pipeline that spans drying, pressing, fermenting and brewing, and a
medicine tier deep enough that poisoning yourself on the wrong mushroom is a survivable mistake
rather than a footnote. Its natural endpoint is a closed cultivation economy - spore prints and spent
substrate feeding back into new logs - and an oil/herb supply line that the other mods in the repo
draw on rather than reimplement. The mod is already feature-complete for its stated scope; what it
lacks is *consequence* (nothing yet demands the antidote tier it ships) and *renewability* (log
cultivation is one-way).

**Potential major additions** (not yet justified - revisit after Phase 3):
- **Toxicity consequences** - a Poisoned/Parasites debuff on eating raw Morel, raw Chicken of the
  Woods or Death Cap. The mod already ships the entire antidote tier (Death Cap Tincture, Healer's
  Moss Tincture, Anti-Nausea Tea) with nothing that creates demand for it. This is the single change
  that would make the medicine line matter.
- **Old-Growth Grove** - a gated seventh WorldMap node hosting the rarest forage (Truffle, Morel,
  Cloudberry), gated on `RequiredAlreadyVisitedEnvironments`. Gives foraging a late-game destination.
  Check `DefaultWorldMap.json` for free cells before authoring coords.
- **Spent-substrate sink** - depleted logs as compost, mulch or firewood, closing the cultivation
  loop from the other end.

These live in `Documentation/Ideas/HerbsAndFungi/` (IDEAS.md and AdvancedCooking.md).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod HerbsAndFungi`, then `/consolidate-audit HerbsAndFungi`, and update this roadmap |
| Game version update | Run `/update-mod-version HerbsAndFungi`, refresh `lib/Assembly-CSharp.dll` from the framework canonical, **regenerate `lib/Assembly-CSharp-nstrip.dll` with NStrip** (it will not be caught by the byte-match test), rebuild, then re-audit the biome `LocalizationKey` list for new substring collisions |
| After fixing a critical issue | Run `/critical-analysis HerbsAndFungi` to verify the fix |
| Any player-visible behavior change | Update ModInfo.json Description + README.md + CHANGELOG.md in the SAME commit, and add a `/playthrough-test-plan` row - the export gate checks version strings, not content claims |
| Any `SimpEn.csv` row added or edited | Add the matching `SimpCn.csv` row in the same commit - this mod ships Chinese and parity is currently perfect at 1283/1283 |
| After Phase 2 complete | Run `/export-to-repo HerbsAndFungi` and bump the minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod HerbsAndFungi          - full health check, updates .audit/
/consolidate-audit HerbsAndFungi  - synthesize .audit/ into summary + ideas + this roadmap
/critical-analysis HerbsAndFungi  - adversarial review
/feature-map HerbsAndFungi        - regenerate the (currently stale) feature map
/repair-items HerbsAndFungi       - auto-fix item JSON issues
/repair-structures HerbsAndFungi  - auto-fix structure JSON issues
/build-mod HerbsAndFungi          - build Release DLL
/deploy-mods HerbsAndFungi        - build + deploy to game
/playthrough-test-plan record     - the ONLY way a human verification advances past pending
/update-mod-version HerbsAndFungi <ver> - bump version in all 3 files
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
