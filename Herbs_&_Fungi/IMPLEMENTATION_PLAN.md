# HerbsAndFungi — Expansion Implementation Plan

Source: routed suggestions from `Documentation/Suggestions_Routed.md` (H&F section).

Scope: ~30+ new items plus 3 new systems (medicine, vinegar, plant beds). Phased so each phase is a clean release point.

---

## Phase 1 — Simple forage (low-risk, mostly JSON + sprites)

- **Berries**: Blackcurrant, Redcurrant, Lingonberry, Cloudberries (clone Billberries pattern)
- **Decorative / single-use wild plants**: Wild Flowers (dryable), Dandelion (food/dye/wine), Common Plantain, Chamomile, Mandrake

Each item = one CardData JSON, CSV rows, sprite, forage drop injection in `GameLoadPatch.cs`. ~10 items, no new mechanics.

## Phase 2 — Edible roots & flavorings (cooked-only or special-prep)

- **Cooked-only**: Bracken/Fiddleheads (toxic raw → cooked safe via `ReceivingCardChanges.TransformInto` on cook), Rhubarb (stem-only edible), Camas Bulbs (root vegetable)
- **Spices / flavor items**: Mustard, Horseradish, Licorice, Chicory (drink ingredient), Goosefoot (anti-parasitic — `Parasites` stat reduction on consume)
- **Woad** (dye output): ship as raw material; downstream dye consumer deferred or paired with a minimum consumer this round

## Phase 3 — Trees & fruit/nut produce

- **Crabapples, Chestnuts, Plums**: clone vanilla `TreeApple` / `AcornsEdibleRaw` patterns. Each needs tree + felled + harvestable variants and forage CIs.
- **Yew (toxic, bow-only)**: restrict to `RequiredCardWarpData` for a Yew Bow blueprint; no edible CI.

## Phase 4 — Crops (farming chains)

- **Kale/Cabbage, Carrots, Hops, Grapes, Oats, Sugarbeets** — each crop is ~4–6 JSON files (seed → planted → growing → mature → harvested + maybe drying variant). Largest phase by file count.
- Audit vanilla farming first to confirm stage pattern; H&F may already have Wheat as a template.

## Phase 5 — Aquatic

- **Water Lily**: water-adjacent forage gate (`tag_River` precedent from WDI / oil press).

## Phase 6 — Medicine system

- **Brew Expansion**: cauldron-based brews from Appleweed/Frostleaf/etc. Decide whether to use a new H&F cauldron, ACT's tea station, or vanilla CookingPot. **Tradeoff**: new cauldron is cleaner but doubles the effort vs. extending an existing station.
- **Burn Ointment** (honey + frostleaf), **Frostbite Ointment** (fat + fireroot), **Bug Repellent** (peppermint/thyme): recipe items with applied effects on a Use DismantleAction (stat changes).
- **Cast** (plaster + cloth): two-ingredient bone-healing buff item.

## Phase 7 — Processing & new structures

- **Vinegar**: spoilage transform on Wine/Beer (`OnZero` of Spoilage transforms to LQ_Vinegar). Smallest effort in this phase.
- **Mossbed**: 3-variant placeable (kit → placed → grown) using the Progress-countdown pattern from the pickle vat. Slowly grows healer's moss.
- **Plant Pot / Plant Bed**: indoor structures with planted-card slots. Accept seeds via CardInteraction; mirrors crop stages from Phase 4.

---

## Cross-cutting considerations

1. **Sprite art is the bottleneck**, not code — ~30 PNGs at minimum. Decide on AI-generated vs. vanilla-recolor vs. commissioned art before starting Phase 1.
2. **Forage list saturation** — H&F already injects into vanilla forages. Adding 15+ more drops dilutes vanilla finds; consider per-region targeting (Mandrake in caves, Cloudberries in cold biomes) rather than blanket injection.
3. **Cross-mod surface area** — Brew Expansion conceptually overlaps with ACT's Tea Blending Station. Lock in "H&F cauldron = medicine, ACT tea station = tea" upfront to avoid a mid-build pivot.
4. **Yew + Woad have downstream consumers (bows, dye)** that don't exist yet. Either ship as inert materials (player waits for a future update), or pair with a minimum consumer item this round.

## Ordering rationale

- Phases 1–3 are pure JSON+sprite work using existing framework patterns; no new C# subsystems.
- Phase 4 is the largest by file count but uses vanilla farming primitives once Wheat (or another template) is verified.
- Phase 5 is one item, kept separate so it doesn't block Phase 4.
- Phases 6–7 introduce new structures and stat effects; do these last so the item catalog and farming chains they may consume are already in place.
