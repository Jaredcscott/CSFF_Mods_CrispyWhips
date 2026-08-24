# Roadmap: Herbs and Fungi
Version at time of writing: 1.10.2
Date: 2026-08-04
Audit score: 9/10 — PASS (0 CRITICAL, 0 DESIGN GAP, 1 open warning family)

## Current State

**Theme**: A forage-and-preserve survival mod — gather mushrooms, berries, and medicinal herbs; grow hemp and cultivate mushroom logs; dry, grind, press, ferment, and brew them into food, oils, teas, and remedies. Early-to-mid-game gathering and food/medicine production with a light late-game cultivation and WorldMap-exploration layer.

**Content**: 109 items / 16 liquids / 45 blueprints / 29 structures / 16 perks / ~100 custom images / 4 WorldMap clone locations.

**Stability**: 9/10 — PASS. No CRITICAL, no DESIGN GAP. One open warning *family*: the six mushroom-log inoculation blueprints display "5 mushrooms" in-game while actually requiring 10 (JSON `CardDescription` + authoritative CSV rows). Clean build (0/0), clean bin/Release sync, localization 0 missing / 0 duplicate keys, WorldMap 0 UID/coord/travel/gate errors, acquisition coverage fully closed (192 produced / 71 consumed, 0 unreachable, 0 dead-end).

**Open work**: No 🔴 Open or 🟡 Pending retrospectives implicate Herbs and Fungi or a framework subsystem it consumes. (Forest Scout trail = 🔵 Graduated 2026-07-23; the 🟡 questinjector/RETRO_CLOSURE items concern framework features H&F does not use.)

**Framework compliance**: **Tier 2 — current.** Extends `ContentModPlugin` (base emits the one canonical startup Info line and owns Harmony/UnpatchSelf); pickle-vat routing goes through `Api.ActionRouter`; the Apothecary quest-gate uses `SpawnService.CardSpawned`; blueprint-tab and perk injection are fully declarative (framework-handled). No deprecated patterns: no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no `ModLoaderVerison`/`ModEditorVersion` fields, no manual perk/blueprint injection. Reflection field lookups are cached (`CachedField`). Code-quality passed A1–I4.

---

## Phase 0: Stabilize  *(score ≥ 8 and no open retros — normally skipped; retained for one confirmed live defect)*

> One player-visible content-accuracy defect is live now and confirmed still open in current source (2026-08-04). Fix before any new content lands.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Mushroom-log "5"→"10" fix — six `Bp_MushroomLog_*.json` `CardDescription.DefaultText` + the matching rows in `SimpEn.csv` **and** `SimpCn.csv` (CSV wins at runtime — JSON alone won't fix what players read). README already says 10. | Docs/CSV sync (summary W1–W6) | P0 | Quick |

*Run `/repair-blueprints HerbsAndFungi` or a focused CSV edit; bump the patch version and update CHANGELOG in the same commit per §Docs-Honesty. This closes the last open finding and returns the mod to 10/10.*

---

## Phase 1: Foundation

> Table-stakes for a healthy mod. **Nearly all already satisfied** — listed so a future reader can confirm the baseline holds.

| Item | Type | Status | Complexity |
|------|------|--------|------------|
| Versions synced across ModInfo.json / Plugin.cs / README.md (all 1.10.2) | Version hygiene | ✅ Done | — |
| Framework Tier 2 (ActionRouter / SpawnService / ContentModPlugin) | Framework compliance | ✅ Done | — |
| Chinese localization (`Localization/SimpCn.csv` present, backfilled through v1.9.3) | Localization | ✅ Done | — |
| Refresh the stale per-category sub-audits (items 2026-06-14, blueprints 2026-05-20, structures/images 2026-06-22, perks 2026-05-20 — perks-report still counts 15 of 16) against 1.10.2 | Audit hygiene | P2 | Quick |
| Run `/audit-environments HerbsAndFungi` — the 4 CT8 clone locations have no dedicated report (WorldMap health is covered by preflight + feature-map A6, so not a scored gap, but this closes the last uncovered audit dimension) | Audit coverage | P2 | Quick |
| Update `Documentation/Ideas/HerbsAndFungi/IDEAS.md` header ("as of v1.6.8 / EA 0.63") to the current baseline | Doc hygiene | P3 | Quick |

---

## Phase 2: Core Expansion

> The most impactful content additions that extend the existing loop with pure JSON/CSV/sprite work (no new C#).

### Amadou / Tinder Fungus
**What**: One CT0 foraged bracket fungus tagged with the vanilla fire-starting tag, injected into forest forage in `GameLoadPatch.cs`.
**Why**: Thematically core for a fungus mod and fills a real survival niche (fire-starting) the mod doesn't touch. Highest thematic payoff for the least work.
**Requires**: none.
**Complexity**: Quick.

### Tea craft-path completeness
**What**: Add "Mix into Hot Water"/kettle blueprints for the 6 perk-only teas (Chamomile, Dandelion, Ginseng, Reishi, Lion's Mane, Yarrow) and normalize the tea CardType so all ten behave identically in containers.
**Why**: Six of ten teas exist only as perk grants — a non-perk player can never brew them — and the CT0/CT9 type mix causes container-fill inconsistency (feature-map A1/A2). Turns a partial system into a complete, discoverable one; completes the mod's flagship medicinal system.
**Requires**: none (pattern already shipped on the 4 existing tea BPs + ground-herb "Mix into Hot Water" CIs).
**Complexity**: Medium.

### Culinary seasoning powders + Mushroom Broth buff variants
**What**: Grind dried Black Trumpet / Shiitake / King Oyster into umami powders (clone the GinsengGround grind-CI, add Savoury/Earthy FlavourTags); add 2–3 Mushroom Broth sibling BPs (Reishi/Lion's Mane/Chanterelle) with themed `StatModifications`.
**Why**: The mod ships medicinal grounds but no culinary ones, and `Bp_MushroomBroth` proves the buff pattern — these are the concrete JSON-only slice of the larger "advanced cooking" concept, no station or C# needed.
**Requires**: none.
**Complexity**: Medium.

### Hemp Rope / Hemp Twine + Berry Jam
**What**: (a) A Spin/Twist CI on Fiber (or a simple N-Fiber blueprint) → hemp cordage; HempStalks already drops vanilla Fiber. (b) Berries + honey + heat → a shelf-stable sweet preserve (Sweet/Fruity, long SpoilageTime).
**Why**: Rope is a constant vanilla pain point (high utility, low cost); the mod has raw/dried/fermented berry paths but no concentrated sugar-preserve path.
**Requires**: none — both independent, reuse shipped content.
**Complexity**: Medium (batch).

---

## Phase 3: Integration & Depth

> Cross-mod hooks and late-game progression for experienced players. All soft-dependency per project doctrine — every hook degrades gracefully when the partner mod is absent.

### Old-Growth Grove — gated late-game forage node
**What**: A fifth WorldMap node hosting the rarest forage (Truffle, Morel, Cloudberry), gated on prior visits via `RequiredAlreadyVisitedEnvironments`, at collision-free coords west of the Alder/Oak cluster.
**Why**: The four shipped clearings are all reachable early with flat difficulty — this gives foraging a late-game aim. Follows the shipped 4-node clone pattern.
**Requires**: check `DefaultWorldMap.json` for free cells; clone-env board-seeding rules (`reference_clone_env_board_seeding`).
**Complexity**: Complex.

### Cultivation loop-closers (Spore Print + Spent Substrate + Grow Bag)
**What**: A DA on mature logs yields a Spore Print (inoculating then consumes 1 print instead of 10 mushrooms); depleted logs → Spent Substrate → compost → Fertilizer (or a firewood/mulch sink); optional indoor Grow Bag as a faster log alternative.
**Why**: Mushroom-log cultivation is currently one-way with no downstream use for spent substrate — this makes it renewable and closes the loop's head and tail.
**Requires**: design decisions (print replaces vs. parallels the recipe; species-specific prints?; fertilizer hook into vanilla plantation timers; grow-bag balance vs. logs).
**Complexity**: Complex.

### Herbalism head-start perk
**What**: A Situational-tab perk granting a `Skill_Herbalism` (`85559650c938ef843af92c18f5b0c6c7`) bias via `StartingStatModifiers` — build to `CSFF_Patterns.md` § Skill Head-Start Perk.
**Why**: All 16 current perks grant *items*; none grant a *skill*. Cheap, high pick-rate, diversifies the character-creation offer.
**Requires**: none.
**Complexity**: Medium.

### Cross-mod hooks
**What**: (a) H&F oils (`HerbalOil_*` / HempSeedOil / PeanutOil) → CMC CeramicLamp / ACT lantern refuel via `tag_Oil`; (b) H&F `tag_DryingRackSanctuary` dried herbs → a WDI Grinding Mill "Grind Dried Herbs" operation BP (WDI-side); (c) H&F dried herbs/grounds → ACT Tea Blending Station inputs; (d) H&F dried Yarrow/Reishi or teas as Feed/Treat CI on Sirus SheepHusbandry livestock; (e) verify RA can repeat H&F grind/cut CIs by ActionTag, add a tag if not.
**Why**: Composes the fleet with unified fuel/processing paths instead of per-mod duplication.
**Requires**: coordination with CMC / WDI / ACT / Sirus versions.
**Complexity**: Medium.

### Toxic-fungi sickness loop *(investigate)*
**What**: A `SelfTriggeredAction` that applies a Poisoned/Parasites debuff on eating an unsafe fungus (Death Cap, raw Morel, raw Chicken-of-the-Woods), creating real demand for the mod's existing antidote tier.
**Why**: Antidotes exist but nothing currently demands them — players brew remedies "just in case" rather than in response to anything.
**Requires**: investigate the STA stat-application hook first.
**Complexity**: Complex.

---

## Phase 4: Polish

> Art, cleanup, and text that make the mod feel finished.

| Item | What | Complexity |
|------|------|------------|
| Custom oil sprites | Replace the vanilla `Bowl_Clay` placeholder on the 6 oils (HempSeedOil, TruffleOil, HerbalOil_Appleweed/Fairyweed/Frostleaf/Hemp) — summary M1; first verify in-game they aren't rendering blank | Quick each |
| HempStalks sprite | Replace `Nettle_Stems` placeholder with mod-owned art (summary M1) | Quick |
| Distinct flagship tea sprites | GinsengTea, ReishiTea, LionsManeTea, SleepTea off the shared generic thirst/clay icons | Medium |
| Hemp Butter variant cleanup | Confirm `hemp_butter_active` is consumed by the dose chain or intentional; prune if orphaned, else add a clarifying note (summary M3) | Quick |
| Empty blueprint-field cleanup | Strip empty `BlueprintStages`/`BlueprintResult`/… arrays from `DryingStackPlaced.json` + `WoodenPantryPlaced.json` (cosmetic, summary M2) | Quick |
| GIF: Oil Press (active) | Idle/working animation while pressing — flagship workstation | Medium |
| GIF: Pickle Vat (fermenting) | Subtle bubbling on the sealed state | Medium |
| GIF: Mushroom logs (mature) | Fruiting animation on the 6 ready-log states | Medium |
| Art for Phase 2/3 items | Seasoning powders, jam, Amadou, twine, Spore Print, grow bag, fertilizer | Quick each |

GIF authoring per `Documentation/CSFF_GIF_Authoring.md`.

---

## Long-term Vision

> Where Herbs and Fungi should be at v2.0.

The mod already spans forage → preserve → process → medicine → cultivate more completely than most; the natural v2.0 endpoint is closing every loop it opened and adding the *depth* that turns systems into decisions. That means: a complete, non-perk-gated tea/remedy craft tree; renewable cultivation (spore prints, spent-substrate compost, grow bags) so logs aren't one-shot; a real toxicity/sickness feedback loop that gives the medicine tier a reason to exist; and a gated late-game forage grove that rewards exploration. The two biggest additions not yet justified by current scope both become natural at that scale: a **seasonal ecosystem** that makes preserved food genuinely valuable in winter, and **full crop-farming chains** that turn forage-only herbs into cultivable crops.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **Seasonal foraging depth** — season-gated forage weights (Spring Greens / Autumn fungi / Winter dormancy) via SelfTriggeredAction. Note: `CardDrop` has no season fields (fix-plan 2026-06-20 stripped an earlier aspirational season-param attempt) — do NOT re-claim seasonality without a real, verified gate.
- **Crop-farming chains** (kale/cabbage, carrots, hops, grapes, oats; medicinal-plant beds; tea plantation) — full agricultural backbone for the brewing/medicine systems.
- **Peanut farming chain** — peanuts entered as forage-only in v1.10.0; a plant→grow→harvest loop (clone the Hemp Field pattern) completes the newest system.
- **Mycelium material / Fungal Leather** — cultivated substrate → a leather substitute; only worth building if a concrete consumer ships alongside (avoid the advertised-dead-code trap).
- **Mossbed structure** for renewable Healer's Moss cultivation.

These live in `Documentation/Ideas/HerbsAndFungi/IDEAS.md` (+ `AdvancedCooking.md` for the buff-stew chain).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod HerbsAndFungi`, then `/consolidate-audit HerbsAndFungi` |
| Game version update | Run `/update-mod-version HerbsAndFungi <ver>`, re-run `/diagnose-log`; verify PickleVatSealed fermentation still ticks and forage injection still matches location LocalizationKeys |
| After fixing a critical issue | Run `/critical-analysis HerbsAndFungi` to verify the fix end-to-end |
| After Phase 2 complete | Run `/export-to-repo HerbsAndFungi` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod HerbsAndFungi              — full health check, updates .audit/
/consolidate-audit HerbsAndFungi      — re-synthesize after audits
/critical-analysis HerbsAndFungi      — adversarial review
/repair-blueprints HerbsAndFungi      — auto-fix blueprint JSON (use for the Phase 0 mushroom-log fix)
/repair-items HerbsAndFungi           — auto-fix item JSON
/audit-environments HerbsAndFungi     — cover the 4 WorldMap clone locations
/build-mod HerbsAndFungi              — build Release DLL
/deploy-mods HerbsAndFungi            — build + deploy to game
/update-mod-version HerbsAndFungi <ver> — bump version in all 3 files
/export-to-repo HerbsAndFungi         — push to public repo
```
