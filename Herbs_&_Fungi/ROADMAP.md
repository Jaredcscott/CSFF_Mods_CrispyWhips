# Roadmap: Herbs and Fungi
Version at time of writing: 1.8.0
Consolidated: 2026-06-13
Audit score: 7/10 — PASS

## Current State

**Theme**: Foraging-driven herbalism, fungiculture, and preservation — forage mushrooms/berries/herbs, cultivate mushroom logs and hemp, then dry, grind, press, pickle, and brew into food and medicine.
**Content**: 103 items / 16 liquids / 43 blueprints / 29 structures / 15 perks / 93 custom images
**Stability**: 7/10 — 0 CRITICAL, 1 DESIGN GAP (orphaned generic pickle vat item), 5 WARNINGS (README misinformation, 12 empty CSV stat labels, Tier 2 architecture debt), 4 MINOR
**Open retrospectives**: none (oil_press_reset graduated 2026-06-02; no open/pending retros match H&F)
**Framework compliance**: Tier 1 — framework JSON/WarpData/sprite/CSV/perk/blueprint injection all used correctly. Tier 2 NOT adopted: PickleVatRoutePatch patches 3 forbidden methods. Migration to Api.ActionRouter is the primary architecture debt.

---

## Phase 0: Stabilize

> Fix the player-visible misinformation and the unreachable orphan before any new content lands.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Populate 12 empty stat-name CSV entries (SpoilageTime_StatName / UsageDurability_StatName) in Localization/SimpEn.csv | Localization fix | P0 | Quick |
| Rewrite README.md Blueprint Tabs table to match actual BlueprintTabs.json (Support=3, Cooking=4, Medical=6); add Healer's Moss Tincture to Medicinal Liquids table; update "EA 0.63" to "EA 0.65"; fix Version History header to v1.8.0 | Doc accuracy | P0 | Quick |
| Delete orphaned herbs_fungi_pickle_vat_closed (PickleVatClosed.json, bin/Release copy, CSV rows) | Design gap cleanup | P1 | Quick |
| open_pickle_jar "Return Bowl" DA: add ProducedCards returning vanilla ClayBowl GUID a968f3eaffc6b9743b82982b5af2ab8c | Item fix | P1 | Quick |
| Migrate PickleVatRoutePatch off ActionRoutine/CardOnCardActionRoutine/PerformStackActionRoutine to Api.ActionRouter handlers (Make-Brine in-place swap + pickle brine-gate) | Framework Tier 2 | P1 | Complex |

---

## Phase 1: Foundation

> Framework tier compliance and doc hygiene to ensure the mod stays maintainable.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Confirm SimpCn.csv stays at parity with SimpEn.csv after Phase 0 CSV changes | Localization | P1 | Quick |
| Update Documentation/Ideas/HerbsAndFungi/IDEAS.md header version reference from "v1.6.8 (EA 0.63)" to v1.8.0 | Doc hygiene | P2 | Quick |
| (Optional) Adopt Api.ContentModPlugin base for Plugin.cs (override OnModAwake/RegisterPatches/OnModDestroy; do NOT declare own Awake/OnDestroy) | Framework Tier 2 | P3 | Medium |

---

## Phase 2: Core Expansion

> Top content additions from ideas.md that extend the core loop.

### Amadou Tinder Fungus + Forager Perk
**What**: (a) Amadou bracket fungus — one CT0 foraged item tagged with the vanilla fire-starting tag, injected in GameLoadPatch.cs. (b) A Situational-tab character-creation perk granting a foraging/herbalism skill head-start (~+75 flat VM per reference_perk_modifier_conventions).
**Why**: Amadou fills a survival niche the mod does not touch and is thematically perfect. All 15 current perks grant items — none grant a skill bias; cheap, high pick-rate diversifies the roster.
**Requires**: Nothing — independent of Phase 0/1.
**Complexity**: Quick

### Culinary Seasoning Powders + Berry Jam
**What**: (a) Grind dried gourmet mushrooms (Black Trumpet, Shiitake, King Oyster) into umami seasoning powders — clone GinsengGround grind-CI pattern + Savoury/Earthy FlavourTags so they feed the vanilla Pouch flavor-transfer. (b) Berries + honey + heat → shelf-stable jam (high Satiation, long SpoilageTime, Sweet/Fruity FlavourTags).
**Why**: Pure JSON+CSV+sprite. Fills the culinary grounds gap and the concentrated berry gap. Reuses already-shipped items.
**Requires**: Nothing — independent.
**Complexity**: Medium

### Advanced Buff Cooking — Mushroom and Herb Stews
**What**: Named stew/broth recipes granting temporary stat buffs based on the medicinal ingredient (e.g. Reishi Stew → +Immune System, Ginger Broth → +Warmth, Chanterelle Soup → +Morale). Buffs via StatModifications on the consume DA — same mechanism as existing teas. Per Documentation/Ideas/HerbsAndFungi/AdvancedCooking.md.
**Why**: The #1 recurring player request. FlavourTags already ship on all 63 edibles; this adds the buff payoff.
**Requires**: Phase 0 (tea/grind chains must work first), Phase 1 ActionRouter migration (build tracker on the router, not a new forbidden patch).
**Complexity**: Complex

---

## Phase 3: Integration and Depth

> Cross-mod hooks and renewable cultivation loops for experienced players.

### Renewable Cultivation Loop — Spore Prints, Substrate, Grow Bags
Mature logs yield a Spore Print (DismantleAction); inoculating consumes 1 print instead of 5 mushrooms. Depleted logs → Spent Substrate → Progress-countdown composting → Fertilizer that speeds hemp/garden growth. Optional: indoor Grow Bag as faster log alternative.
**Decision needed**: print replaces or parallels 5-mushroom recipe; species-specific prints; how fertilizer hooks vanilla plantation timers; grow-bag balance vs. logs.
**Complexity**: Complex

### Cross-Mod Oil and Livestock Care Hooks
(a) HerbalOil_* / HempSeedOil refuel CMC CeramicLamp via CI gating on tag_Oil or H&F oil UIDs. (b) H&F dried Yarrow/Reishi or medicinal teas as Feed/Treat CI on Sirus SheepHusbandry livestock. Prefer mod-local fallbacks so missing partner mods degrade gracefully.
**Complexity**: Medium

### Seasonal Foraging and Respawn Hotspots
Season-gated forage (spring greens, autumn fungi, winter dormancy) and placed/world nodes where species reliably regrow after harvest.
**Investigate**: TriggerService (Trigger JSON + MaxOnBoard caps) vs. SelfTriggeredAction seasonal/area gates.
**Complexity**: Complex

---

## Phase 4: Polish

> Art, animation, and description accuracy.

| Item | What | Complexity |
|------|------|------------|
| Description accuracy sweep | Realign item/blueprint descriptions and CSV after Phase 0 | Quick |
| Distinct liquid sprites | Replace Bowl_Clay/Thirst_Old defaults on flagship teas (GinsengTea, ReishiTea, LionsManeTea, SleepTea) with custom PNGs | Medium |
| GIF: Oil Press (active) | Idle/working animation while pressing — flagship workstation | Medium |
| GIF: Pickle Vat (fermenting) | Subtle bubbling on sealed/fermenting state | Medium |
| GIF: Mushroom logs (mature) | Fruiting animation on 6 ready-log states | Medium |
| Art for Phase 2/3 new items | Seasoning powders, jam, Amadou, Spore Print, grow bag, fertilizer | Quick each |

GIF authoring per Documentation/CSFF_GIF_Authoring.md.

---

## Long-term Vision

Herbs and Fungi becomes the definitive foraging-and-herbalism mod for CSFF: a complete forage → cultivate → preserve → cook/medicate loop where the player runs a self-sustaining apothecary and mushroom farm. Advanced buff-cooking (Phase 2) is the connective tissue that gives every harvested ingredient a destination. The two additions that become natural at v2.0 scale are a **seasonal ecosystem** that makes preserved food genuinely valuable in winter, and **full crop-farming chains** (kale, hops, grapes, medicinal-plant beds, tea plantation) that turn forage-only herbs into cultivable crops.

**Potential major additions** (not yet justified — revisit after Phase 3):
- Crop-farming chains (kale/cabbage, carrots, hops, grapes, oats; medicinal-plant beds; tea plantation)
- Toxic look-alike identification (False Chanterelle, Ergot) with risk-on-eat unless identified — needs a no-custom-UI gating mechanism investigated first
- Mossbed structure for renewable Healer's Moss cultivation

These live in `Documentation/Ideas/HerbsAndFungi/IDEAS.md`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod HerbsAndFungi`, then `/consolidate-audit HerbsAndFungi` |
| Game version update | Run `/update-mod-version HerbsAndFungi <ver>`, re-run `/diagnose-log`; verify PickleVatSealed fermentation still ticks |
| After fixing a critical issue | Run `/critical-analysis HerbsAndFungi` to verify the fix crafts end-to-end |
| After Phase 2 complete | Run `/export-to-repo HerbsAndFungi` and bump minor version |

---

## Skill Cheatsheet

```
/audit-mod HerbsAndFungi              — full health check
/consolidate-audit HerbsAndFungi      — re-synthesize after audits
/critical-analysis HerbsAndFungi      — adversarial review
/repair-items HerbsAndFungi           — auto-fix item JSON
/repair-blueprints HerbsAndFungi      — auto-fix blueprint JSON
/generate-ideas HerbsAndFungi         — expand Ideas backlog
/build-mod HerbsAndFungi              — build Release DLL
/deploy-mods HerbsAndFungi            — build + deploy
/update-mod-version HerbsAndFungi <ver>  — bump version
/export-to-repo HerbsAndFungi         — push to public repo
```
