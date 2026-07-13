# Roadmap: Advanced Copper Tools (ACT)
Version at time of writing: 1.9.0
Date: 2026-06-13
Audit score: 8/10 — PASS (0 CRITICAL, 1 DESIGN GAP, 3 WARNING)

## Current State

**Theme**: A mid-game copper-and-bronze metalworking tier focused on comfort, throughput, and light — for players who have unlocked copper smelting and want quality-of-life infrastructure (cooking, bathing, storage, transport, lighting) plus a light combat/armor option.

**Content**: 31 items / 29 blueprints / 12 structures / 8 perks / 34 custom images / 557 localization rows (EN) + SimpCn.csv (ZH)

**Stability**: 8/10 — SHIP-READY with open warnings. WearableMetalPan 0-durability and README version fixed. Remaining: 2 divergent CSV key pairs (MetalLantern_CardHelpSection + Bp_WheelHub_CardDescription, W1), 2 stale Chinese perk descriptions (W2), Tea Station design gap (no tea output, G1).

**Open work**: None. All 3 ACT retrospectives (`act-fire-placement`, `copper-pan-inventory-full`, `act-copper-armor-stats`) are 🔵 Graduated/resolved. No open or pending ACT retrospectives in `Documentation/Retrospectives/INDEX.md`.

**Framework compliance**: **Tier 2 (ActionRouter).** `SawEffectPatch` and `TeaStationPatch` migrated to `Api.ActionRouter` handlers (2026-06-13). No mod-side patches on `GameManager.ActionRoutine`, `CardOnCardActionRoutine`, or `PerformStackActionRoutine`. `HeatHeldLiquidPatch` remains direct (disabled by default; legacy path only). SpawnService migration (`GameLoadPatch` drop-injection paths) still pending.

---

## Phase 0: Stabilize  *(skipped — audit is SHIP-READY and no open retrospectives)*

> No CRITICAL items. The README-accuracy fix below is carried into Phase 1 because it is a feature-honesty issue, not a stability blocker.

---

## Phase 1: Foundation *(completed 2026-06-13)*

> Table-stakes for a healthy, maintainable mod.

| Item | Type | Status |
|------|------|--------|
| ~~Correct the v1.9.0 README changelog — it claimed "ActionRouter/SpawnService integration" not yet in code~~ | Feature honesty | ✅ Done — migration landed, claim is now accurate |
| ~~Migrate `SawEffectPatch` + `TeaStationPatch` to `Api.ActionRouter` handlers~~ | Framework Tier 2 | ✅ Done — both patches use `ActionRouter.Register` |
| Migrate `GameLoadPatch` GiveCard/spawn paths to `Api.SpawnService` where applicable | Framework Tier 2 | ⏳ Pending |
| Verify dual-tab Copper Brazier injection in-game (Fire + Furniture) | Verification | ⏳ Pending |

---

## Phase 2: Core Expansion

> The 1–3 highest-impact additions that close ACT's most visible gameplay-loop gaps. All three are pre-scoped in `Documentation/Ideas/AdvancedCopperTools/IDEAS.md`.

### Herbal Tea Beverages (close the Tea Station's dead-end)
**What**: 3–5 brewed teas (Calming, Warming, Focus) as drinkable CT0 items or liquids with `EdibleStats` + an explicit `Drink` `DismantleAction` (`ModType:3`, `ActionSoundsWarpData:["Drink"]`). Produced via a CookingRecipe in the lit station's slots: dried herb + hot reservoir water → tea.
**Why**: The Tea Blending Station dries, grinds, heats water, and has "Grind All" — but ships **no actual tea product**. The mod's flagship station is a means with no end. This is the biggest single content gap.
**Requires**: none (Phase 1 not a hard blocker, but do the ActionRouter migration first so the new recipe logic lands in the routed handler, not another direct patch). Follows `reference_food_ediblestats_pattern`.
**Complexity**: Medium

### Tool & Armor Repair loop
**What**: Tool-to-transform CI (drag hammer onto a worn Large Saw / armor piece → restore durability) or a small repair blueprint (worn piece + 1 metal sheet + nails → fresh piece).
**Why**: Nothing restores `UsageDurability` on the saw or the four armor pieces today — they can only be smelted and re-crafted. A maintenance loop is the natural payoff for investing in a copper armor tier.
**Requires**: none. Follows `reference_tool_to_transform_pattern`.
**Complexity**: Medium

### Bathtub warm-vs-cold mechanical differentiation
**What**: Gate an extra `-Stress` / `-Filth` (and/or body-warmth) bonus on the warm tub's Heat (FuelCapacity ≥ threshold) at the "Take Warm Bath" action, so a warm bath measurably out-performs a cold one.
**Why**: The warm bath is currently mostly narrative. Differentiating it justifies the firewood/heat investment and completes the 3-state bathtub design.
**Requires**: confirm whether the warm tub already carries a usable heat stat at the bath action or needs one added (quick JSON read).
**Complexity**: Quick–Medium

---

## Phase 3: Integration & Depth

> Cross-mod hooks and progression content for experienced players. Integration points are already enumerated in IDEAS.md.

### Copper Cauldron as a shared brew/dairy vessel
**What**: Add a shared cooking-container tag (e.g. `tag_MedicineBrewStation` for H&F, a dairy/scalding tag for SheepHusbandry) to the ACT Copper Cauldron's placed location card, so other mods gate their recipes on the tag rather than a specific vanilla UID.
**Why**: The cauldron currently mirrors the clay cauldron with more slots but no unique payoff. Becoming the premium vessel for H&F medicine brewing and Sirus/SH dairy gives it a reason to exist and creates emergent cross-mod value.
**Requires**: coordination with HerbsAndFungi (Phase 6 brew expansion) and Sirus23_Mod_Collection dairy recipes; soft-dep with mod-local fallback.
**Complexity**: Medium

### Copper hardware supply tier for WDI
**What**: List ACT `metal_sheet` / `copper_nails` UIDs in WaterDrivenInfrastructure blueprint `RequiredElements` (sluice hardware, waterwheel bands, pipe collars) as a soft dependency with a mod-local fallback when ACT is absent. Optionally ship the long-deferred **Copper Spigot** valve component once WDI's pipe network design lands.
**Why**: ACT is the natural metal-hardware source in this mod ecosystem; WDI is the natural consumer. Mutual benefit, no hard coupling.
**Requires**: coordination with WDI; respects `feedback_cross_mod_output_dependency` (cross-mod output UIDs are silent soft deps — prefer mod-local fallback).
**Complexity**: Medium

### Coppersmith progression perk
**What**: A perk granting a Metalworking/Smithing skill head-start (+75 per `reference_perk_modifier_conventions`) instead of an item grant.
**Why**: All 8 current perks grant items. A progression perk makes the armor/tool tier reachable earlier and diversifies the perk roster.
**Requires**: exact skill GUID + Suns/Moons cost decision.
**Complexity**: Quick–Medium

---

## Phase 4: Polish

> Art, animation, and text. ACT is already strong here — the images audit passes with 0 issues and all 34 sprites resolve, so this phase is light.

| Item | What | Complexity |
|------|------|------------|
| Normalize 3 `Bp_*` LocalizationKey prefixes | `Bp_CastStoveTop`, `Bp_MetalSheet`, `Bp_WearableCopperPan` use a non-standard prefix vs. the rest. Cosmetic; CSV coverage confirmed. | Quick |
| Bump `Bp_MetalLantern` research tier | `BlueprintUnlockTicksCost=12` is below the intermediate norm of 16; align if desired. Functional as-is. | Quick |
| GIF animation candidates | Lit Tea Station, lit Copper Brazier, lit Copper Stove — idle flame/glow animation on the `_Lit` variants (see `Documentation/CSFF_GIF_Authoring.md`). | Medium |
| Description accuracy pass after Phase 2 | Re-run a CSV/JSON `DefaultText` alignment once tea beverages + repair recipes add new strings. | Quick |
| New tea/repair art | Custom PNGs for any new Phase 2 tea beverages and repair-product variants. | Quick each |

---

## Long-term Vision

> Where ACT should be at v2.0.

ACT's natural endpoint is **the complete copper/bronze metalworking tier** — a self-contained progression from raw nuggets through hardware (sheets, nails), tools, armor, comfort furniture, and cooking infrastructure, with a maintenance loop (repair) and a finished cooking loop (actual tea/brew products) so every station has both an input and an output. At v2.0 it should be the metalworking *prerequisite* that other mods build on: H&F brewing, SH dairy, and WDI hardware all consuming ACT intermediates, with ACT itself the entry gate to a future **Ironworks** tier.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **Copper Forge / Smithing Upgrade Station** — a dedicated ACT forge hotter/faster than vanilla, doubling as the prerequisite tier an Ironworks would depend on. Needs investigation of `tag_SmeltingContainer`/`tag_SmeltingContainerIron` + the `SpoilageTime`-as-temperature model (`reference_iron_nugget_passive_heating`).
- **Metal Alloying System** — copper + tin/zinc → bronze/brass with a durability boost, with copper-only recipe lockouts so alloy crafts don't silently eat tin nuggets.
- **Two-Wheeled Pushcart** — higher-capacity transport above the wheelbarrow.

These (and the smaller deferred items: Copper Mattress-Frame Bed, Bedwarming Pan, Metal Sieve, Copper Watering Can, Copper Cold Cellar) live in `Documentation/Ideas/AdvancedCopperTools/IDEAS.md`. Keep that file as the spec backlog; promote items here as they become justified.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod AdvancedCopperTools` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis AdvancedCopperTools` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo AdvancedCopperTools` and bump minor version |
| After framework Tier 2 release | Re-check ActionRouter/SpawnService adoption (Phase 1 migration) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod AdvancedCopperTools              — full health check, updates .audit/
/consolidate-audit AdvancedCopperTools      — re-synthesize after audits
/critical-analysis AdvancedCopperTools      — adversarial review
/repair-items AdvancedCopperTools           — auto-fix item JSON issues
/repair-blueprints AdvancedCopperTools      — auto-fix blueprint JSON issues
/generate-ideas AdvancedCopperTools         — expand Ideas backlog
/build-mod AdvancedCopperTools              — build Release DLL
/deploy-mods AdvancedCopperTools            — build + deploy to game
/update-mod-version AdvancedCopperTools <ver> — bump version in all 3 files
/export-to-repo AdvancedCopperTools         — push to public repo
```
