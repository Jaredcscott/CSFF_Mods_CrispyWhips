# Herbs and Fungi — Changelog

All notable changes to this mod. Dates are release dates.

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
