# Advanced Copper Tools — Changelog

All notable changes to this mod are documented here.

## [1.16.8] - 2026-09-18

### Changed
- **Salt-Cured Meat has its own picture.** It was borrowing the game's Cooked Meat picture; it is
  now a thick red slab caked in coarse salt, on both the item and its blueprint.

## [1.16.7] - 2026-09-18

### Changed
- **Download is about a third of its previous size: 37.2 MB to 11.8 MB.** Two separate causes, both
  in packaging rather than in the mod. The build script wrote a second, byte-identical copy of every
  card image into `Resource/Texture2D/` alongside `Resource/Picture/`, which nothing ever read, so
  every release archive carried the whole art set twice. Separately, the three Copper Bathtub images
  had shipped at 2400x1792 instead of the 512-wide size every other card in this mod uses. Both are
  fixed. No gameplay, content or balance change, and the bathtub art is unchanged apart from its
  resolution.

## [1.16.6] - 2026-09-08

### Docs
- **README under-claimed a confirmed feature (docs-honesty, under-claiming direction).** The v1.15.9
  entry still read "Not yet verified in-game" for Copper Stove / Copper Brazier / Metal Lantern
  Firekeeping-duty compatibility, but the playthrough tracker confirmed it in-game on 2026-08-15
  (T2.85). Updated the v1.15.9 entry and added a player-facing sentence to the Small Copper Stove,
  Copper Brazier and Metal Lantern sections stating a recruited Partner tends their fuel under the
  vanilla Fire keeping duty. Also noted the Ownership panel on the Brazier and Lantern (who may claim
  and refuel them) is still unconfirmed (T2.212, pending). No code change.

## [1.16.5] - 2026-09-06

### Fixed
- **Bronze/White-Bronze armor is now actually craftable as designed, and the Copper tier is no
  longer strictly obsolete at the same unlock point.** `MetalSheet.json`'s Metal Type stat
  (`SpecialDurability4`, plus the unused `SpecialDurability2/3`) was authored with field names that
  don't exist on the game's `DurabilityStat` type (`DurabilityName`/`MaxDurability`/`StartDurability`/
  `Restricted`/`IsHidden` instead of the real `Active`/`FloatValue`/`MaxValue`/`RestrictToSpecificValues`/
  `ValidValues`/`HidingOptions`), so it silently deserialized inactive: every Bronze armor blueprint's
  bronze-grade metal-type gate was a no-op, and a plain Copper Sheet satisfied it just as well.
  Rewrote the field names so Metal Type is genuinely active (`ValidValues` 100/110/120/130/140), and
  added a new **Bronze Sheet** blueprint (`act_bp_bronze_sheet`, Metal & Clay -> Metal Crafts) that
  hammers a heated Ghost Bronze/Tin Bronze/White Bronze bar into the same sheet card, now correctly
  tagged bronze-grade (110-140) - the only way to produce a sheet that satisfies the Bronze armor
  recipes. A plain Copper Sheet (Metal Type 100, from the base Metal Sheet blueprint) no longer
  qualifies. Not yet verified in-game.
- **Cross-mod safety**: WaterDrivenInfrastructure's Cast Metal Sheet is now tagged Metal Type 100
  (copper-grade), so ACT's sheet-interchangeability patch can no longer hand WDI players a sheet that
  bypasses the Bronze gate above. WDI needs its own version bump to ship this - tracked separately.
- **All four Bronze armor blueprints' research-unlock gate now actually requires a bronze-grade
  sheet.** `Bp_BronzeHelmet.json`, `Bp_BronzeBreastplate.json`, `Bp_BronzeGauntlets.json`, and
  `Bp_BronzeGreaves.json` gated "researchable" on holding ANY Metal Sheet (no metal-type filter),
  while their own `UnlockConditionsDesc` said "Bronze Sheet Needed" and the crafting-time gate
  correctly required Metal Type 110-140. A plain Copper Sheet could prematurely unlock research on
  all four. Added the same `Special4Range: {110, 140}` filter the new Bronze Sheet blueprint already
  uses, so the unlock text now matches what actually gates it.
- **Bronze Armor (torso) now requires medium leather, matching its own recipe everywhere else.**
  `Bp_BronzeBreastplate.json` required `GpTag_LeatherSmall` while README and the item's own dismantle
  recovery (`SkinLeatherMedium`) both said medium - a copy-paste leftover from the Helmet recipe that
  let a player upgrade small leather to medium for a Tin Solder and some build time. Now requires
  `GpTag_LeatherMedium`, matching Copper Armor's equivalent recipe.
- **Removed the non-functional armor quality-scaling scaffolding instead of leaving it disclosed as
  broken.** All 13 armor items' `ArmorValueDurabilitiesMultiplier` block read from `SpecialDurability2`
  (Metal Quality), which every ACT armor item declares inactive - the multiplier could never move off
  its 1.0x floor, and the in-game `EffectName` text ("Armor (+X) interpolated by quality from 1 to
  1.5") kept advertising scaling that never happened. Cleared the multiplier array, disabled the
  matching `EffectScalesWithDurabilities` block, and reworded `EffectName` to the flat value it
  actually applies (e.g. "Armor (+30)"). Also removed `GameLoadPatch.cs`'s
  `RepairArmorMultipliers`/`FixArmorMultiplier` - a 1.16.4 fix that only ever restored this same
  no-op entry, and would otherwise have kept re-adding the multiplier block this pass just removed.
- **Armor-repair reflection misses are no longer silent.** `Patcher/GameLoadPatch.cs`'s
  `FindEquippedArmorCards` returned an empty list on ANY reflection miss with no breadcrumb, so a dead
  `CharacterWindow` / `EquipmentSlotsLine` / `Slots` chain was indistinguishable from a legitimate
  "no armor equipped": `RepairArmorCards` reported both as `changed 0`, and a field rename after a game
  update would have disabled the equipped-armor half of the save/reload repair with zero log output.
  The file already had the right machinery for this (a per-cause `HashSet` plus `WarnArmorRepairOnce`,
  carrying a comment describing this exact hazard) wired into 2 of its 3 reflection exits; the third had
  none. Added `CharacterWindow-null`, `EquipmentSlotsLine-null` and `EquipmentSlots-null` so the
  warn-once covers every early return rather than only the catch. No behaviour change when reflection
  succeeds; a failure is now an error line naming the cause. Checked while in there: ACT is clear of the
  EA 0.67x `GraphicsManager.GetSlotForCard` arity break that is throwing in a sibling mod, because it
  never calls that method and its only reflected method call, `UpdatePassiveEffects`, matches the live
  zero-arg card-level overload.

### Changed - documentation / honesty corrections
- **`ModInfo.json`'s Description no longer claims armor tracks the metal it was built from.** All 13
  ACT armor items (Copper/Bronze/Iron) declare their Metal Type stat inactive and carry no
  metal-variant names - only the Oil Flask and Cauldron actually track metal. Corrected to say so.
- **`ModInfo.json` and README no longer call the bathtub's Wash with Soap "a bigger cleansing and
  mood boost."** It's smaller than the free Warm Bath on the same card (Filth -100 / Morale +30-40,
  and consumes a Soap, versus the free bath's Filth -200 / Morale +60-80 plus three more stats) - its
  real advantage is that it doesn't require the fire to be stoked. Reworded everywhere this claim
  appeared.
- **README's Metal Sheet section (Base Materials) no longer contradicts its Bronze Armor Set
  section.** Both now describe the real, current path: copper-grade sheets from the base Metal Sheet
  blueprint, bronze-grade sheets from the new Bronze Sheet blueprint above. Also corrected a
  README claim that armor value rises with durability/quality - it doesn't; see the Fixed section of
  [1.16.4] above for the corrected record on that.
- **Disclosed undocumented art reuse.** Bronze Helmet/Bracers/Greaves/Armor (and their blueprints)
  reuse the Copper set's sprites, and the Ore Chest reuses the Copper Chest sprite - both now noted
  in the README next to the Iron tier's distinct art, matching how reuse is disclosed elsewhere in
  this doc (e.g. the Cave Prospector perk icon reusing tin vein art). No new art shipped this pass.
- **Removed the "Metal Pan" row from README's Character Perks table.** That perk (backed by
  `Perk_MetalPanTester.json`) was deleted in [1.16.4] as unreachable dev content, but the README row
  describing its starting kit was never removed, so the README kept advertising a 5-Suns perk that
  had not existed for a release already. 7 perks now ship and 7 are documented.
- **The bathtub's Wash with Soap "bigger cleansing and mood boost" claim is now actually reworded
  everywhere it appears, not just in ModInfo.json.** The prior line item above was incomplete: the
  same phrase was still live in README's Copper Bathtub Actions list and in the card's own in-game
  `ActionDescription` (JSON + both localization CSVs). All three now read "a quick clean and mood
  boost that doesn't need the fire lit."
- **README's `GameLoadPatch` description no longer says "copper armor" only.** The armor-repair net
  it describes has covered all 13 Copper/Bronze/Iron items since [1.16.4]; the README wording was
  never updated to match.
- **Smelting Recovery table now lists all smeltable items.** Added the 8 rows that were missing
  despite having real `SmeltingRecipes.json` entries: Iron Sheet, Iron Helmet, Iron Bracers, Iron
  Greaves, Iron Armor (Male/Female), Copper Watering Can, and Copper-Rim Chamberpot.
- **README no longer advertises the armor-multiplier repair that this same release deleted.**
  `README.md`'s Harmony-hook list still described `GameLoadPatch` as a postfix that "repairs a missing
  `ArmorValueDurabilitiesMultiplier` entry on every ACT armor piece" - but the fix above removed
  `RepairArmorMultipliers`/`FixArmorMultiplier` outright, so that clause described code that no longer
  exists. Two same-release fixes left unreconciled: one corrected the tier scope (copper -> all three
  tiers), the other deleted the mechanism being scoped. The bullet now describes what the postfix
  actually does, and additionally documents `PatchSheetInterchangeability` (ACT Copper/Iron Sheet slots
  also accept WDI's matching Cast Sheet, tier-locked, no-op without WDI), which shipped undocumented.
  The player-facing behavior it backs - armor keeps mitigating across a save/reload, via
  `RepairArmorCards` + `RefreshArmorPassiveEffects` - is unchanged and still real.
- **Copper Cauldron no longer claims "six cooking slots".** `CopperCauldron.json` ships an empty
  `InventorySlots`/`InventorySlotsWarpData` and is governed by `MaxWeightCapacity: 3000`, which is the
  standard vanilla weight-limited container shape (vanilla `Basket`, `Chest`, `Shelf` and `CookingPot`
  all ship it). Capacity was never broken, but the item count is not capped at six: it depends on
  ingredient weight. README's Overview bullet and Features list now say so. The Tea Blending Station's
  "six herb slots" is untouched and remains accurate - that one IS a real 6-entry `InventorySlots` array.
- **Tea Station's per-card transform log demoted to Debug.** `Patcher/TeaStationPatch.cs` logged
  `[TeaStation] Transformed -> {uid}` at Info once per converted card, so a single "Grind All" sprayed
  the log, against CLAUDE.md's per-item-loop logging norm. The line's own comment set the release
  condition ("Info until the 1.15.8 pour-quantity fix is play-verified"), and that gate is satisfied:
  the playthrough tracker records T2.83 = pass and no retrospective names the line as a confirmation
  signal. Now `LogDebug`, stale comment removed.
- **`IronNailSmeltPatch` renamed to `TinOreSmeltPatch`.** The class handled only `act_tin_ore`
  smelting and nothing about iron nails, so a contributor searching for iron-nail logic by filename
  landed in the wrong file. Renamed the file and the class (`git mv`, so history follows) and updated
  the `Plugin.cs` registration and the README hook list; the README entry now also mentions the
  Metal Quality (SpecialDurability2) carry-over it was already doing. No behavior change. The dead
  `IronOreUid` constant this class used to carry was already gone.
- **Ore Chest README no longer implies a food filter it does not have.** The description said the
  crate was for bulk raws "rather than food", but its `InventoryFilter` only excludes the four
  animal-size tags, so food fits fine. Reworded to state the real mechanic: food fits but gains
  nothing, because the Ore Chest has no spoilage protection (that is the Copper Chest's job).
- **Armor-repair reflection now leaves a breadcrumb on every early return.**
  `FindEquippedArmorCards` returned an empty list on a `CharacterWindow` / `EquipmentSlotsLine` /
  `Slots` reflection miss with no log, so `RepairArmorCards` reported "changed 0" - indistinguishable
  from a genuine "no armor equipped", meaning a field rename after a game update would silently
  disable the equipped-armor half of the repair. Added three per-cause `WarnArmorRepairOnce`
  breadcrumbs so the warn-once covers every early return, not just the catch. Logging only, no
  behavior change.

### Known state
- `EffectScalesWithDurabilities` blocks remain on all 13 armor items with `Active: false`. This is
  deliberate and matches vanilla: all 2709 vanilla `PassiveEffects` entries sampled carry the key, so
  the disabled-in-place form IS the vanilla shape and deleting it would deviate from it. The defect
  that mattered (an `EffectName` advertising "interpolated by quality from 1 to 1.5" that could never
  happen) was fixed above by rewording to the flat value.

## [1.16.4] — 2026-09-01

### Fixed
- **Bronze and Iron armor now get the same save/reload combat repair as Copper.** The armor repair
  net (re-adds modded armor to `GameManager.ArmorCards` after a save load and at encounter start,
  refreshes passive effects) only listed the 4 Copper UIDs - the Bronze (4 items) and Iron (5 items,
  incl. both breastplate fits) tiers shipped later were never added, so their armor dropped out of
  the game's combat armor list, and stopped mitigating at all, after a save/reload. All 13 armor
  items are covered now. Internal repair methods renamed tier-neutral. Not yet verified in-game.
  **Correction, 2026-09-06 (see [1.16.5]):** this entry originally also credited the same patch with
  restoring an emptied `ArmorValueDurabilitiesMultiplier` as an armor-quality-scaling fix. That part
  was a no-op - the multiplier reads a Metal Quality stat that every ACT armor item declares
  inactive, so it always interpolates to the same 1.0x floor whether the array is present or empty.
  Removed that claim; see [1.16.5] for the corrected record.

### Removed
- **`Perk_MetalPanTester` deleted (dead dev content).** The hidden "Metal Pan" character-creation
  perk had `HiddenUntilUnlocked` with empty unlock conditions - impossible to unlock or ever see
  in-game. Leftover dev-testing perk (same pattern as the previously removed Cave Prospector);
  flagged by the 2026-09-01 fleet feature audit. Its localization rows were removed with it. No
  player-visible change.

## [1.16.3] — 2026-08-25

### Performance
- **Combat-encounter copper-armor repair no longer rescans the whole board on every hit.**
  `EncounterArmorRepair_Prefix` (fires on `EncounterPopup.GenerateAndApplyPlayerWound`, i.e. every
  wound during every fight) walked the full live-card list with reflection on each call. Switched to
  the framework's `CardFinder.AllCards()`, which caches that same scan and only re-walks it when the
  board's card count actually changes — repeat wounds within a fight (and fights on an unchanged
  board) now hit the cache instead of re-scanning. No behavior change, same cards found.
- **Tin-ore smelt metal-type/quality tagging no longer re-walks the class hierarchy by reflection on
  every smelt.** `IronNailSmeltPatch` had its own hand-rolled `AllCards` field lookup (run twice per
  smelt); replaced with the same cached `CardFinder.AllCards()` the sibling `IronVeinQualityPatch`
  already used.

## [1.16.2] — 2026-08-24

### Removed
- **Cave Prospector perk removed.** The Collapsed Rock Face gates it described have applied to every
  character by default since v1.15.6 — the perk itself controlled nothing, and unlike Forest Scout
  in HerbsAndFungi it was never given a compensating stat bonus, so its 1-Star cost bought literally
  no mechanical effect. The cave network (portal + dig-through access) is unchanged; only the
  do-nothing perk pick is gone.

## [1.16.1] — 2026-08-16

### Fixed
- **Blank card art on Rendered Fish Oil and Salt-Cured Meat blueprints.** Both referenced sprite
  names that don't exist (`FishMeatFattyRaw`, `ClayBowl`) and rendered as a blank/locked card.
  Rendered Fish Oil now uses the vanilla Clay Bowl art (`Bowl_Clay`) to match its own "render in a
  clay bowl" description; Salt-Cured Meat's blueprint and finished item now use the vanilla Dried
  Meat art (`Meat_Cooked`).

## [1.16.0] — 2026-08-16

### Added
- **Ore Chest** — a high-capacity, animal-safe sealed metal crate for bulk mining raws (Greenstone,
  Bog Iron, Tin Ore, Salt, Stone). No spoilage protection, just a lot of weight-based storage.
  Craftable at Construction → Furniture.
- **Salt-Cured Meat** — raw meat cured in Salt into a slow-spoiling travel ration; gives the Salt
  Mine a self-contained ACT payoff beyond feeding vanilla cooking. Craftable at Survival → Support.
- **Bronze / White-Bronze armor tier** — Bronze Helmet, Bracers, Greaves, and Armor, slotting
  between the Copper and Iron tiers. Gated on a bronze-grade metal sheet (any of Ghost Bronze / Tin
  Bronze / White Bronze) via the existing SD4 metal-type system, not a new sheet item. Furnace
  recyclable like every other ACT armor piece.
- **Wash with Soap** — a new interaction on the Copper Bathtub (Warm/Hot): drag vanilla Soap in for
  an extra-deep clean and a bigger mood boost. No new item, no new art.

## [1.15.9] — 2026-08-14

### Internal
- Marked the Copper Stove, Copper Brazier, and Metal Lantern (both lit and unlit variants of each)
  as compatible with the vanilla Firekeeping duty, so a recruited Partner NPC can be assigned to
  tend their fuel. Not yet verified in-game — no player-facing behavior claim until confirmed.

## [1.15.8] — 2026-08-14

### Fixed
- **Powder ground at the Tea Station no longer vanishes when poured into bottles, cloth bags, or
  wooden barrels.** (Nexus bug report, 2026-08-10.) Powders such as Wheat Flour or medicine powder
  are solids with a pourable liquid form: the game stores their pour quantity on the card instance
  (`CurrentLiquidQuantity`, initialized to `SolidToLiquidInfo.LiquidQuantity` on a fresh card), and
  the built-in pour-into-container action always destroys the solid card and adds that quantity of
  liquid to the container. The Grind All in-place transform reset this quantity to 0, so pouring
  station-ground powder destroyed the card and added nothing — powder produced by a grinding disc
  or spawned directly was unaffected. Grind All now initializes the pour quantity exactly as
  vanilla card creation does. Note: powder ground BEFORE this update carries its zero quantity in
  the save file and will still vanish — grind fresh material after updating.
- **Tin Ore can now be smelted in any smelting container, not only iron-capable ones.** Its
  `PassiveEffects` container gates referenced `tag_SmeltingContainerIron` instead of the general
  `tag_SmeltingContainer` used by every other smeltable item in the mod, and it was also missing
  the `tag_SmeltsAt1100` item tag those items carry. Corrected both — Tin Ore now behaves like the
  30+ other smeltable items in the mod and matches its own description ("Smelt it in a forge or
  furnace").

## [1.15.7] — 2026-08-09

### Documentation
- Documentation review for publish pass. Cross-checked README.md and ModInfo.json `Description`
  against shipped `CardData/`, `CharacterPerk/`, `WorldMap/`, and C# source for the 26 commits
  since the last public export (2026-07-17) — both were already fully up to date (Salt Mine/Rock
  Quarry, bathtub Hot Bath tier, iron armor Male/Female split, Rendered Fish Oil, tin solder/nail
  cross-mod interchangeability, watering can and reservoir description corrections, Collapsed Rock
  Face "Always" seal fix). No content changes required. Version bump only.

## [1.15.6] — 2026-08-09

### Fixed
- **Collapsed Rock Face walls (Copper/Iron/Tin/Salt/Quarry cave passages) now spawn for every
  player by default, not only after equipping the Cave Prospector perk.** The perk was never
  actually required to dig through a wall (that's gated on tool tags — pickaxe/shovel/axe/antler/
  knife — on the wall's own `CardInteraction`); it only controlled whether the wall existed at all,
  which meant these passages were silently wide open on any save — including existing saves that
  install this mod without ever taking Cave Prospector. Requires `CSFFModFramework` 2.21.1+ (new
  `SealTrigger` type `"Always"`).

## [1.15.5] — 2026-08-07

### Fixed
- **5 stale Chinese translations re-synced to current English.** `Localization/SimpCn.csv` rows for
  `Bp_Handles` (gate item changed from Copper Sheet to Plank), the Building Materials and Metal Pan
  Tester starting perks (leather now specified as raw/untanned), the Large Saw help text (recipe now
  includes copper nails), and the Copper Brazier pack-up action (now preserves remaining oil) had
  drifted from their English source after earlier content edits. Caught by the fleet Chinese-parity
  checker's stale-anchor check (E23).

## [1.15.4] — 2026-08-07

### Added
- **Calming Tea, Focus Tea, and Warming Tea now carry `tag_Preservable`.** This is a vanilla CardTag
  with no shipped vanilla users; third-party mods that bulk-match spoilage-rate effects onto it (e.g.
  freshness/preservation perk mods) now apply correctly to our three perishable teas, which previously
  had no coverage.

## [1.15.3] — 2026-08-03

### Added
- **Iron-tier armor fastener slots now also accept Copper Nail and both WaterDrivenInfrastructure rivets** (previously only accepted Iron Nail) — cross-tier fastener interchangeability is now bidirectional, matching the copper-tier slots' existing acceptance of Iron Nail/WDI rivets.
- **New Tin Solder ↔ WaterDrivenInfrastructure Alloy Solder interchangeability** — any blueprint requiring Tin Solder now also accepts WDI's Alloy Solder (soft dependency; no effect without WDI installed).

### Fixed
- **Copper Watering Can no longer advertises unimplemented interactions.** `CardDescription`, `CardHelpSection`, and README previously claimed it could "douse fires" and "pour into any water container" — neither interaction exists; the can only supports Fill from Water, Drink, and Empty Out. Text corrected to match.
- **Tea Blending Station's README description corrected**: the built-in reservoir is an 8-charge Water Charges tank, not a "300-capacity" reservoir — that figure belonged to the station's unrelated stove fuel stat and had bled into the wrong sentence.
- `Oil.json`'s card art reference corrected (`ClayBowl` → `Bowl_Clay`) — was pointing at a nonexistent sprite name and rendering blank.

### Changed
- Calming Tea, Focus Tea, and Warming Tea's spoilage stat is now labeled "Spoilage" instead of "Freshness," for consistency with the rest of the mod.

## [1.15.2] — 2026-07-28

### Changed
- **Rock Vein yield is now deterministic: 3 Stone + 3 Heavy Stone per strike, guaranteed, on both the Mine Stone (pickaxe) and Chip Away actions** — replacing the previous mixed guaranteed/chance drop table (2 Stone + 60% chance +1 + 50% chance Heavy Stone on Mine Stone; 1 Stone + 25% chance Heavy Stone on Chip Away). With the vein's existing 6-strike depletion count, this makes every Rock Vein worth exactly 18 Stone + 18 Heavy Stone regardless of which tool is used.

## [1.15.1] — 2026-07-28

### Changed
- **Collapsed Rock Face tunnels now drop 3 Heavy Stone (up from 1)** when cleared, alongside the existing 3 Stone — applies to all six "Dig Through" actions (Copper, Tin, Iron, Salt, Salt↔Quarry, Quarry).
- **Rock Vein now also yields Heavy Stone**, matching the Collapsed Rock Face tunnels: 50% chance per pickaxe ("Mine Stone") strike, 25% per "Chip Away" strike. Rock Vein already shared the same 6-strike depletion count as every other vein; this brings its drop table in line with the tunnels too.

## [1.15.0] — 2026-07-28

### Added
- **Two new cave locations extend the mine network east.** A **Salt Mine** (3 × Salt Vein, dropping vanilla Salt) sits east of Copper Vein Cave, and a **Rock Quarry** (3 × Rock Vein, dropping vanilla Stone in bulk — 2–3 per pickaxe strike) sits east of Tin Vein Cave (Metal Mines). The two are connected to each other north–south (Rock Quarry south, Salt Mine north). Both new passages — and the link between them — use the same Collapsed Rock Face / Cave Prospector perk gating as the existing Tin/Copper/Iron network: open by default, sealed by the perk, one-time dig with a pickaxe, shovel, axe, antler, or knife to clear.

## [1.14.3] — 2026-07-27

### Fixed
- **Bathtub pick-up now preserves invested state.** Previously, picking up a filled or warmed placed bathtub silently discarded its firewood, water level, ash, and charcoal progress — and picking up a Warm tub degraded it straight to the cold Full kit instead of a matching Warm kit. The two portable kit variants (`copper_bathtub_full`, `copper_bathtub_warm`) now carry the same Firewood/Charcoal/Ash/Water-Level stats as their placed counterparts, and every kit↔placed transform (Place, Pick Up, Light Fire, and heat-depleted cooldown) transfers those values both directions. A picked-up Warm tub now stays a Warm kit; setting it back down restores it exactly as it was. Also corrected a pre-existing, previously-unflagged bug of the same class on the Empty variant's Pick Up (and everywhere else this shipped): the JSON key `TransferProgress` does not exist on the game's `CardStateChange` struct (only `TransferFuel`/`TransferCharges`/`TransferSpecial1-4` do) — it silently deserializes to nothing, so charcoal progress was never actually transferred anywhere it was declared, including in the Warm bathtub's own pre-existing heat-depleted cooldown. All occurrences renamed to the real field, `TransferCharges`.
- **Iron Sheet's research-discovery gate now actually requires an iron-grade nugget.** It previously fired on any vanilla Metal Nugget regardless of metal type (the `UnlockConditionsDesc` said "Iron Nugget Needed" but nothing enforced it at the discovery stage — only the build-time recipe checked metal type). Now filters on `SpecialDurability4` 200 (iron), mirroring the already-correct Tin Solder pattern.
- **Collapsed Rock Face (Copper Vein Cave, Iron Vein Cave) no longer shows tin-vein art.** Both now use the vanilla `Tunnel_1` rockfall sprite already used by the Tin Vein Cave's own collapsed wall, matching what the passage actually looks like.

## [1.14.2] — 2026-07-27

### Changed
- **Small Copper Stove now has an NPC trading value (1500)** — it was the mod's one 0-cost
  tradeable item. Anchored to vanilla copper goods (Copper Bottle 800, Copper Jar 900). Part
  of the fleet-wide trading reprice.

## [1.14.1] — 2026-07-20

### Changed
- **Iron Armor now offers separate Male/Female torso variants** (`IronArmor_M.png` / `IronArmor_F.png`) instead of one unisex piece. Same Torso +45 protection and recipe as before — art only.

## [1.14.0] — 2026-07-17

### Fixed
- **Iron Sheet's card art**: it referenced the now-removed `IronSheet.png` sprite (replaced by the image-upgrade pass's `MetalSheet_Iron.png`) on both the item and its blueprint — was silently falling back to a missing-sprite placeholder in-game.

### Added
- **Copper/Iron Sheet interchangeable with WaterDrivenInfrastructure's Cast Copper/Iron Sheet** (same tier only — Iron Sheet only pairs with Cast Iron Sheet) when WDI is installed. Nails were already cross-mod/cross-tier interchangeable with WDI's rivets (v1.11.5 and earlier); this closes the equivalent gap for sheets, and additionally makes Copper Nail accept WDI's new Iron Rivet. Soft dependency — no behavior change when WDI isn't installed.

### Removed
- **Copper Mattress-Frame Bed** — shipped in [1.12.0] (full kit/blueprint/placed CardData, sprite, and localization) and removed here: Community_Mod_Chest already ships the equivalent (`CopperBedFrame` → `Bp_ComfortableBed` → `ComfortableBed_Placed`), so this mod's own bed was redundant and will not return.

## [1.13.0] — 2026-07-16

### Added
- **Rendered Fish Oil blueprint** (Survival → Support tab): render 2 fatty fish meat + a clay bowl into the same Rendered Oil the fat and hemp recipes produce — a third lamp-fuel source for fishing playstyles. Unlocks when fatty fish meat is on board (16 ticks research).
- **Chamberpot waste → Manure**: the Copper-Rim Chamberpot's Empty action now leaves a pile of vanilla Manure, plugging the pot into the vanilla compost/fertilizer chain.

## [1.12.0] — 2026-07-14

### Added
- **Herbal Tea Beverages**: three brewable teas close the Tea Blending Station's long-standing "no output" gap — Calming Tea (dried willow bark), Warming Tea (dried wild garlic), and Focus Tea (dried spirit mushrooms). Brew any of them at the lit station once the reservoir is hot, consuming one Water Charge.
- **Bathtub Hot Bath tier**: the warm bathtub's bath action now splits into a lukewarm Warm Bath (5–50% heat) and a genuinely better Hot Bath (≥50% heat) with a bigger mood boost and a real Stress reduction — keeping the fire stoked now matters mechanically, not just narratively.
- **Bronze-tier Oil Flask & Cauldron**: both items now carry a Metal Type stat and pick up copper/ghost bronze/tin/tin bronze/white bronze naming from whichever metal sheet built them, mirroring the existing Wearable Metal Pan pattern.
- **Copper Watering Can**: fills from any shallow water source; pours into water containers, douses fires, or serves as an emergency drink.
- **Copper-Rim Chamberpot**: a small hygiene convenience item — a minor mood bump for four uses before it needs emptying.
- **Copper Mattress-Frame Bed**: a kit → placed furniture piece with a passive comfort bonus while you're in its environment, better than sleeping on the ground. **Removed in [1.14.0]**: Community_Mod_Chest already ships the equivalent (`CopperBedFrame` → `Bp_ComfortableBed` → `ComfortableBed_Placed`), making this mod's own bed redundant.
- **Iron-Grade Armor Tier**: a new Iron Sheet (forged from iron nuggets) feeds an iron helmet/bracers/greaves/armor set with higher Armor Values and durability than the copper originals — the Iron Vein Cave now has a genuine equipment payoff beyond nails.

## [1.11.6] — 2026-07-12
*(Covers changes since the last published release on 2026-06-23.)*

### Added
- **Cave Prospector perk**: optional 1-Star challenge perk that seals the cave network with Collapsed Rock Face barriers. Clear each barrier once to reopen the route and recover loose stone.
- **Wearable Metal Pan rain collection**: the wearable pan can now catch fresh rainwater during rain.

### Changed
- **Copper construction recipes now use tin solder more broadly**, adding a solder requirement to advanced copper builds such as armor, stove, cauldron, bathtub, brazier, kettle, oil flask, lantern oilwell, wheelbarrow parts, and the large saw.
- **Cave barriers accept more tools**: Collapsed Rock Faces can be cleared with a pickaxe, shovel, axe, antler, or knife instead of only pickaxe/shovel.
- **Tin Solder unlock and recipe now gate on tin nuggets** instead of the old raw Tin Ore requirement.
- **Iron Nails now gate on dried Bog Iron** and the ACT-only Iron Ore item was removed in favor of vanilla Bog Iron.
- **Tin Vein mining time now matches Copper and Iron Veins**, and iron/tin vein behavior has been normalized across cave nodes.

### Fixed
- **Iron Vein drops now spawn as usable vanilla Bog Iron with correct quality** instead of ACT Iron Ore or zero-quality output.
- **Cave Prospector gates now use framework sealable gates**, fixing portal bypass/stale travel-button behavior after digging through the walls.
- **Localization duplicate and truncation issues were cleaned up**, restoring corrupted Copper Stove, Tea Station, Copper Pantry, Metal Lantern, Large Saw, Metal Pan perk, and Chinese text rows.
- **Copper Stove help text now correctly lists charcoal as valid fuel.**

### Technical
- Nail interchangeability now delegates to `CSFFModFramework.Api.BlueprintAlternates` instead of a duplicated local implementation.
- ACT now uses the framework `ContentModPlugin` lifecycle for patch registration.

## [1.11.3] — 2026-07-04

### Changed
- **Tin Vein** mining times raised to match Copper Vein and Iron Vein: pickaxe 6→8, Chip Away 8→24. All three ore veins now take the same time to mine.

## [1.11.2] — 2026-07-04

### Fixed
- **Iron Vein** mining with a pickaxe cost 20 daytime ticks per strike (roughly 2 hours per attempt) — far more than Copper Vein (8) or Tin Vein (6) for a comparable yield. Reduced to 8 to match Copper Vein.
- Bog iron mined directly from an Iron Vein now spawns at **50% Quality** instead of 0%. Vein-mined iron previously skipped the vanilla Fresh → Dried process (which normally raises Quality), leaving it at the lowest possible grade.

## [1.10.1] — 2026-06-21

*(Changes since v1.8.0 — covers v1.9.0, v1.10.0, and v1.10.1)*

### Added
- **Iron Ore** — a raw ore chunk that smelts into an iron-grade metal nugget in a forge or furnace at 900°C. Heavier than copper ore and harder to refine.
- **Tin Ore** — a raw ore chunk that smelts into a tin-grade metal nugget at 900°C.
- **Iron Nail** — a hand-forged iron nail; stronger than copper and interchangeable with copper nails in all construction recipes.
- **Tin Solder** — a tin solder stick for joining metal parts or sealing seams.
- **Forge Iron Nails** (blueprint) — convert iron-grade metal nuggets into iron nails in a forge.
- **Forge Tin Solder** (blueprint) — melt a tin-grade metal nugget into solder sticks.
- **Copper Vein, Iron Vein, Tin Vein** — mineable ore veins found inside the new cave locations; use a pickaxe for best yield, or any digging/striking tool for slower extraction.
- **3 new world map locations**: Copper Vein Cave, Iron Vein Cave, and Tin Vein Cave — explorable cave areas each containing a dedicated ore vein.

### Changed
- **Building Materials** perk: cost reduced from 4 Moons to 2 Moons; kit description corrected ("small leather" instead of "leather").
- **Bathtub Kit** perk: cost reduced from 2 Moons to 1 Moon.
- **Large Saw** perk: converted to a drawback perk (45 Suns cost, was 3 Moons) — the saw is powerful but you pay a survival penalty for starting with it.

### Fixed
- Iron Ore and Tin Ore now correctly produce **iron-grade** and **tin-grade** metal nuggets when smelted. Previously they defaulted to copper-grade, meaning they were only accepted by copper forge recipes and couldn't be used for iron tool blueprints.
- Ore cave veins now drop **vanilla Bog Iron** directly rather than a custom intermediate item, aligning with vanilla smelt chains and improving compatibility.

### Technical
- Saw effect logic migrated to the framework's Tier 2 ActionRouter API (no player-visible change).
- ⚠️ **All item UniqueIDs had underscores stripped** (e.g. `advanced_copper_tools_copper_nails` → `advancedcoppertoolscoppernails`). This is a **save-breaking change** — items from v1.8.0 saves will not be recognized after upgrading. Start a new run after updating.
