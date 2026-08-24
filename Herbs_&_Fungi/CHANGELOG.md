# Herbs and Fungi — Changelog

All notable changes to this mod. Dates are release dates.

---

## [1.10.14] — 2026-08-19

### Fixed

- **Pickle vat: "Uncap" no longer destroys packed food with a single accidental click.** The
  Closed-stage vat's "Uncap" button sat right next to "Seal" and discarded the 5 packed
  ingredients with no warning beyond its own tooltip text — a player checking on progress or
  misclicking lost everything and the vat "vanished" back to empty with nothing to show for it.
  Uncap now shows a confirmation prompt ("Discard the packed [food] and reopen the vat?") on
  all four variants (Frogs/Meat/Mushrooms/Vegetables) before it fires.
- **Sealed (fermenting) pickle vat now has a `DroppedOnDestroy` fallback**, matching the Ready
  stage: if it's ever removed mid-ferment, it returns the empty vat + jar instead of vanishing
  with nothing recovered.
- Fixed "The packed meat are lost" grammar in the Uncap description (→ "is lost").
- Removed 16 orphaned localization rows (`Herbs_And_Fungi_PickleVatSealed_*` /
  `PickleVatClosed_*` / `PickleVatReady_*` without a food-type suffix) left over from an earlier
  single-recipe version of the vat — unreferenced by any shipped card, but described a different,
  now-inaccurate design ("Harvest" destroying the vat outright) that could mislead anyone reading
  the CSV directly.

## [1.10.12] — 2026-08-15

### Fixed

- **Entering the foraging forest through the Portal Hub no longer strands you behind the
  Overgrown Forest Trail.** The trail's challenge card only ever seeded on the Primeval Woods
  side, so a player who teleported straight to the Foraging Path found the East exit sealed with
  no clearable path anywhere. The trail card now also seeds on the Foraging Path side, and (with
  framework 2.23.2) hacking it clear from EITHER side opens travel in both directions at once —
  only one clearance is needed. The cleared trail still regrows after about 10 days, closing the
  route until it's hacked clear again. Requires CSFF Mod Framework 2.23.2+.
- Fixed a framework-side stripped-DA cache collision between the trail gate's two directional
  sides (both previously watched the same env) that could leave the Foraging Path's East travel
  action unrestorable after clearing, and graft a stray travel action onto Primeval Woods.
- Trail card text no longer assumes you're approaching from Primeval Woods ("open the way
  west" → "reopen the way through"); English and Chinese rows updated together.

## [1.10.11] — 2026-08-14

### Fixed

- **Removed 26 `AccessTools.Field: Could not find field … CardName` HarmonyX warnings per load.**
  `GameLoadPatch.CachedField` probed fields via `AccessTools.Field`, which logs a warning for
  every type that legitimately lacks the field (the probe runs against every loaded data type —
  SpiceTag, QuestLog, NPCDuty, …). Switched to native `Type.GetField`, which returns null
  silently; all probed fields are public game fields, so lookup results are unchanged.

## [1.10.10] — 2026-08-13

### Fixed
- **Misty Falls (the Greenfalls copy at the west end of the foraging forest) showed two waterfall
  cards instead of one.** The cloned board seeded one from the vanilla environment template's
  default drops, then the cloned location card's inherited "Create a Waterfall if it is missing"
  maintenance action re-spawned a second one on top of it — the same duplicate-respawn failure mode
  already fixed for ACT's mining caves. `WorldMap/MapNodes.json` now declares `StripLegacyBoardUIDs`
  for Misty Falls so that maintenance action is stripped from the clone. The single remaining
  waterfall card is the hidden-path variant, which still functions as the secret route back to
  Waterfall Caves — that connection is untouched.

## [1.10.9] — 2026-08-09

### Fixed
- **Forest Scout perk (1 Star) had zero mechanical effect since v1.10.7.** The v1.10.7 fix (below)
  correctly made the Overgrown Forest Trail gate apply to every character, not just Forest Scout
  takers — but that left the perk itself granting nothing (no starting items, no stat modifiers),
  so taking it vs. skipping it was behaviorally identical while still costing a Star. Added a
  passive `+1 ForagingAid` bonus to the perk (`PassiveStatModifiers`, RM +1.0 — in line with vanilla's
  constant-aid convention) so the Star cost buys something. The trail/gate mechanic itself is
  unchanged from v1.10.7.

## [1.10.8] — 2026-08-09

### Fixed
- **"Mix into Hot Water" (Dandelion, Chamomile, Yarrow, Ginseng, Reishi, Lion's Mane) failed silently
  when the water wasn't hot enough.** The action requires the water to be at least 50% temperature,
  but that requirement had no fail message — clicking the action with lukewarm/cold water did nothing
  visible, which read as "the tea didn't appear" (player-reported). All six now show "Not hot enough."
  when the heat requirement isn't met. Root cause of the underlying "which direction do I drag" confusion:
  this requirement only evaluates correctly when the water/bowl is dragged onto the herb, not the reverse
  (the herb has no temperature stat of its own) — that direction still silently fails since it isn't a
  bug, just makes the message correct either way.

## [1.10.7] — 2026-08-09

### Fixed
- **The Overgrown Forest Trail now blocks the route to the foraging forest by default, not only
  after equipping the Forest Scout perk.** The perk was never actually required to hack through the
  trail (that's gated on tool tags — blade/axe/shovel/antler — on the trail's own
  `CardInteraction`); it only controlled whether the overgrowth existed at all, which meant the
  route was silently wide open on any save — including existing saves that install this mod
  without ever taking Forest Scout. Requires `CSFFModFramework` 2.21.1+ (new `SealTrigger` type
  `"Always"`).

## [1.10.6] — 2026-08-10

### Fixed
- **Bowl duplication when drinking Dandelion Tea, Chamomile Tea, or applying Herbal Salve** — reported
  by a player (2 bowls became 4 after brewing and drinking two cups). Dandelion/Chamomile Tea are
  brewed via a "Mix into Hot Water" interaction that transforms the liquid *inside* the bowl without
  ever consuming the physical ClayBowl; Herbal Salve's blueprint never required a bowl ingredient at
  all (just ground yarrow + hemp seed oil). All three DismantleActions (Drink/Apply Salve) then
  additionally spawned a brand-new ClayBowl on top of the one that was never consumed. Removed the
  redundant `ProducedCards` ClayBowl entry from all three — the original bowl already survives on its
  own once its contents are consumed. (The other 11 teas/tinctures in `CardData/Liquid/` were audited
  and confirmed correct — either their blueprint consumes a ClayBowl as an ingredient, netting to zero
  when one is returned on drink, or they use the vanilla-style liquid-card-transforms-into-bowl pattern.)

## [1.10.5] — 2026-08-07

### Fixed
- **Repaired drifted Chinese localization (`Localization/SimpCn.csv`)** — second confirmed drift
  incident (see `Documentation/Plans/Fleet/Chinese_Localization_Plan.md`). 88 keys with no Chinese row at
  all (Misty Falls environment, Mushroom Broth / Oil-Press Peanuts / Trail Mix blueprint chains,
  Cleared/Overgrown Forest Trail, Forest Scout perk) were missing entirely — Chinese players saw
  English `DefaultText` for all of them. A further 98 records had English or blank text sitting in
  the Chinese column (dried Golden/King Oyster, truffle family, herbal oils, Wooden Pantry, hemp
  seeds, and others) — translated. All 1,175 legacy 2-column (`Key,Chinese`) rows normalized to the
  canonical 3-column `Key,English,Chinese` format, backfilling the English anchor from
  `SimpEn.csv` so future drift is machine-detectable (`Development_Tools/Check-LocalizationParity.ps1`).
  No existing Chinese translations were altered; all 1,203 original rows kept their exact order.
- **Fixed 3 independently-truncated `SimpEn.csv`/`SimpCn.csv` rows** (`herbsfungiwildflowers_
  UsageDurability_OnFull`, `herbsfungidandeliondried_Eat_Desc`, `herbsfungicommonplantain_
  UsageDurability_OnFull`) — an unquoted comma in the English text had silently truncated it in both
  files (different truncation point in each), so English players were also seeing a cut-off string.
  Restored the full text from each item's JSON `DefaultText` and quoted the field.

## [1.10.4] — 2026-08-07

### Added
- **76 perishable items and liquids now carry `tag_Preservable`** (dried/cooked mushrooms, berries,
  herbs, teas, tinctures, salves, and the four ready pickle vats) — brings them in line with the 15
  items (dried mushrooms, hemp products, roasted peanuts) that already had it. This is a vanilla
  CardTag with no shipped vanilla users; third-party mods that bulk-match spoilage-rate effects onto
  it (e.g. freshness/preservation perk mods) now apply correctly to the rest of our perishable catalog
  instead of only 15 of 91 genuinely spoiling items.

## [1.10.2] — 2026-07-27

### Changed
- **All 84 tradeable items now have NPC trading values** (previously 0 = free at trading
  tables). Prices follow vanilla's scale: berries 5–6 (dried 8–9), mushrooms 6–11, herbs 4–15,
  teas 12–20 (Ginseng Tea 45), tinctures/salves 30–60, truffles 50–120 (luxury), Ginseng
  40–55, pickling gear 8–100, oil-press parts 25–80 and Oil Press Kit 400. Part of the
  fleet-wide trading reprice (CMC 1.31.0 carries the vanilla table; framework 2.18.0 applies it).

## [1.10.1] — 2026-07-21

### Fixed
- **Apothecary quest gate (Stimulant Tea / Anti-Nausea Tea)** — `ApothecaryQuestGatePatch` no longer unlocks both teas together when CMC's Apothecary herb-fetch quest (billberries/springberries/spirit mushrooms) completes; that was the wrong signal. Each tea now unlocks independently and only when the player actually obtains the corresponding H&F item (Ground Ginseng → Stimulant Tea, Dried Ginger → Anti-Nausea Tea), matching the mod's own soft-dependency design (works with or without CMC installed).

---

## [1.10.0] — 2026-07-16

### Added
- **Peanut Oil** — press 3 Raw Peanuts + a Clay Bowl on the Oil Press (new "Press Peanuts" recipe alongside the seed/truffle/herbal pressings). Edible, seasons food, joins the `tag_Oil` lamp-fuel pool.
- **Peanut Butter** — grind Roasted Peanuts with any grinding tool (mortar & pestle, or batch-process in a grinding station). A dense, very drying fat-and-protein meal that keeps for two months.
- **Forager's Trail Mix** — new Cooking-tab recipe: 2 Roasted Peanuts + 2 Dried Billberries → 2 portions of travel ration; lighter on thirst than plain roasted peanuts.
- The peanut cycle no longer dead-ends at "roasted": all three new items consume shipped peanut content.

---

## [1.9.4] — 2026-07-12
*(Covers changes since the last published release on 2026-06-23.)*

### Added
- **Forest Scout perk**: optional 1-Star trait that adds an Overgrown Forest Trail gate between Primeval Woods and the foraging forest. Clear it with a blade, axe, shovel, or antler; the portal route remains available.
- **Medicinal herbs and preparations can now be added to stew**, including chamomile, dandelion, ginger, ginseng, reishi, yarrow, and their dried/ground/cut variants.
- **Updated peanut artwork** for pod, washed pod, raw peanuts, and roasted peanuts.

### Changed
- **Forest route gating moved to framework sealable gates**, replacing the old H&F-specific forest gate patch.
- **Overgrown Forest Trail text now matches the tools it accepts** instead of claiming only blades work.
- **Drying Kit and perk documentation were realigned** with the current 16-perk character creation roster.

### Fixed
- **Localization CSVs were deduplicated and repaired**, removing a near-2x duplicate key set, recovering missing Chinese translations, and restoring truncated English text for mushroom, drying, pantry, herbal oil, hemp field, and map-location entries.
- **Mushrooms and herb items now participate correctly in cooking/stew systems** after tag and interaction repairs across fresh, cooked, dried, ground, and sliced variants.
- **Pickle vat ready-state cleanup** removed the obsolete `PickleVatReady` location card and retired the old truffle fat-cook patch in favor of current data-driven behavior.

### Technical
- C# patching was reduced by removing obsolete `HFForestGatePatch` and `TruffleFatCookPatch` code paths.

---

## [1.8.0] — 2026-06-21

### Added
- **World map expansion**: Four new biomes are now accessible west of Primeval Woods
  - **Foraging Path** — hub node; travel west from Primeval Woods to reach it
  - **Pine Clearing** — north of Foraging Path (pine forest terrain)
  - **Oak Clearing** — west of Foraging Path (oak forest terrain)
  - **Alder Woods** — south of Foraging Path (alder forest terrain)

### Changed
- **Perk costs rebalanced** — most crafting/gathering perks now cost Suns instead of Moons:
  - Add Fungi: 2 Moons → 5 Suns
  - Alchemist: 4 Moons → 10 Suns
  - Culinary Kit: 1 Moon → 10 Suns
  - Drying Kit: 5 Suns → 15 Suns *(more expensive)*
  - Edibles Kit: 3 Moons → 10 Suns
  - Fungal Cultivator: 3 Moons → 15 Suns
  - Master Herbalist: 4 Moons → 10 Suns
  - Medical Mushrooms: 1 Moon → 15 Suns
  - Seed Bag: 5 Suns → 15 Suns *(more expensive)*
  - Smoke Kit: 4 Moons → 1 Moon *(cheaper)*
  - Mushroom Basket: 3 Moons → 1 Moon *(cheaper)*
  - Apothecary: 3 Moons → 1 Moon *(cheaper)*
  - Hemp Farmer: 4 Moons → 2 Moons *(cheaper)*
  - Add Hemp: 3 → 4 Moons *(slightly more expensive)*
- **Pickling**: harvest now yields an **Open Pickle Jar** alongside pickled goods; use the "Return Bowl" action to reclaim the clay bowl lid and reduce per-batch clay cost
- **Pickle Vat consolidation** — generic "Closed Pickle Vat" removed; closing the vat now requires choosing a type (Frogs, Meat, Mushrooms, or Vegetables) up front
- Chinese localization expanded for Hemp Field, Chanterelle, Black Trumpet, Redcurrant, Lingonberry, and all four new map locations

### Fixed
- Food tags corrected on Black Trumpet, Chanterelle, Puffball, Reishi, and Shiitake (and their cooked variants) — mushrooms now properly register in stew and cooking systems

### Technical
- All mod UniqueIDs migrated from `herbs_fungi_*` underscore format to `herbsfungi*` camelCase. **⚠ Save compatibility**: items from runs using v1.7.0 or earlier will not be recognized after updating — start a new character for full compatibility.
- Pickle vat action routing migrated to CSFFModFramework Tier 2 ActionRouter.
- EA 0.65 compatibility; C# patches migrated to Tier 2 runtime APIs (ActionRouter, SpawnService).

---

## [1.7.0]

### Added
- Four foraged berries: **Blackcurrant**, **Redcurrant**, **Lingonberry**, and **Cloudberry**
  - All dryable (passive Dryness stat → dried variant), fermentable (pickle vat), and stackable up to 20
  - Each has Eat and Add to Stew actions; Cloudberry is the rarest and most nutritious

### Fixed
- Invalid JSON in berry CardHelpSection entries (literal newlines → `\n` escapes)

---

## [1.6.10]

- Version bump for release alongside framework 2.0.8 and all in-house mods

---

## [1.6.9]

### Fixed
- EA 0.63f compatibility; blueprint tab injector updated to live UI tabs (fixes journal tab on EA 0.63f)

---

## [1.6.8]

### Fixed
- Minor stability fixes; forage drop injection guard updated

---

## [1.6.7]

- EA 0.63 compatibility pass; no content changes

---

## [1.6.6]

### Added
- Mushroom log cultivation for six wood-growing mushroom types (Shiitake, Lion's Mane, Reishi, Chicken of the Woods, Golden Oyster, King Oyster); logs craft from a vanilla log + spoon auger + 5 mushrooms + wood shavings; ready after ~5 days

---

## [1.6.4]

- Compatibility pass for EA 0.62d (clean rebuild, no source changes)

---

## [1.6.1]

### Added
- Pickle vat fermentation chain (4-variant clay vessel: unfired → fired → closed → sealed → ready)
- Oil press multi-stage build chain with seed, truffle, and herbal oil recipes
- Wooden Pantry furniture storage
- 15 character-creation perks in the Situational tab
- Wild Ginger added to spice/herb roster

### Fixed
- Truffle fat-cook patch — Dried Truffle Slices + fat in heat produces Cooked Truffle instead of ash

---

## [1.4.0]

### Added
- Drying/preservation system (Drying Tray, Drying Stack)
- Medicinal teas: Ginseng, Reishi, Yarrow, Lion's Mane
- Hemp seed/flower/fiber cycle
- 11 mushroom varieties + 2 herbs
- CSFFModFramework integration
- Full localization coverage (~600 entries)

### Removed
- Hemp Addiction challenge perk and HempSatiation stat
