# Herbs and Fungi — Changelog

All notable changes to this mod. Dates are release dates.

---


## [1.13.0] - 2026-09-08

The last row of the mod's Audit Remediation Plan, and a mod-wide repair to the flavour data that
reading it exposed. Neither has been confirmed at a running game: both carry rows in
`.claude/playthrough-test-status.json` (T2.230, T2.231) awaiting human verification.

### Added

- **Truffle Butter.** Drag Fat, a Fat Chunk, a Butter Chunk or Milk Butter onto a fresh Truffle (or
  the truffle onto the fat) and the fat is used up and the truffle becomes a block of Truffle Butter:
  Strong Earthy, Strong Savoury, Medium Buttery. Eat it as it is, or add it to any stew. Keeps about
  a week. Uses the vanilla butter sprite. (T2.230)
- **Truffle Salt.** Drag Salt onto a whole Dried Truffle (or the truffle onto the salt) and grind
  them together for a quarter hour; the salt is used up and the truffle becomes a finishing salt that
  never spoils: Strong Earthy, Strong Savoury, Subtle Salty (the same Salty as plain salt). Add it to
  any stew. Art is a placeholder (reuses the Shiitake Powder sprite). (T2.230)

### Fixed

- **Every flavour this mod declared was mostly inert, and the strongest ones were the deadest.**
  `FlavourTags[].Intensity` is an engine enum: 0 = Medium, 1 = Strong, 2 = Subtle. This mod's 72
  flavoured cards were authored on an ordinal 1 to 7 scale instead. The game reads the raw number
  through a switch whose default arm returns zero, so the 91 entries carrying 3 to 7 (the truffles,
  the dried and cooked mushrooms, the concentrated berries, everything the scale meant as "strong")
  contributed no flavour at all and showed no flavour line on the card, while a 1 (meant as a trace)
  read as Strong and a 2 as Subtle. Remapped in place: 1 and 2 to Subtle, 3 and 4 to Medium, 5 to 7
  to Strong. Three of the four spice tags already used the enum and are unchanged; Peanut Oil's did
  not and was remapped the same way. Net effect in a stew: this mod's ingredients now contribute
  flavour where before most contributed nothing, so flavour scores on stews built from them will
  change. Enforced by `Development_Tools/Tests/FlavourTags-IntensityEnum.Tests.ps1`, which is
  self-demonstrating and sweeps every mod. (T2.231)

### Notes

- **The row that produced Truffle Butter was marked BLOCKED for two waves on a premise that did not
  hold.** Its prompt said to clone the Hemp Butter heat-activate-then-solidify chain and wait for
  that chain's in-game confirmation (T2.153, still unrun). Truffle butter is a compound butter: fat
  plus truffle, no activation, no drying timer. The shape actually cloned is the mod's own "Mix with
  Fat or Butter" drag action, which is the working entry step of that same chain. T2.153 remains
  open on its own merits and no longer gates anything.
- **Truffle Salt deliberately does not require a mortar on the board.** The engine field for that
  (`RequiredTagsOnBoard` on a card action) is used by no vanilla card action and no mod in this
  repo, so it cannot be checked offline, and a mis-shaped condition would hide the action forever
  rather than merely skip the requirement. The grind is a hand action costing one time unit.
- **Both cards' nutrition, trading values and flavour strengths are provisional and have not been
  balance-tested.** The flavour remap's tercile thresholds are a judgment call recorded here so the
  next reader does not re-derive them: they reproduce vanilla's own skew (Subtle most common, Strong
  rare), and the one spice tag that was already enum-valued (`Spice_HempOil`: Nutty Medium, Earthy
  Subtle) agrees with what the remap produces from its own card's ordinal values (Nutty 3, Earthy
  2), which is the closest thing to a second opinion the data offers.


## [1.12.0] - 2026-09-08

The last two buildable rows of the mod's Audit Remediation Plan. Neither has been confirmed at a
running game: both carry rows in `.claude/playthrough-test-status.json` (T2.225, T2.226) awaiting
human verification.

### Added

- **Apothecary Shelf**, a filtered store for the mod's own materia medica. It takes herbs, fungi,
  powders and medicines and refuses ordinary food and building materials, and it slows spoilage on
  what it holds to 60% of the normal rate, against the Wooden Pantry's 75%. It is the narrower,
  cheaper counterpart to that pantry: 3 planks, 2 twine and a hammer, unlocked once you have dried
  yarrow, and it appears in Construction > Furniture. Reuses the vanilla shelf sprite. (T2.225)
- **Herbal Incense Bundle**, the mod's first ambient effect and the room-scale counterpart to the
  personal Herb Pipe. Bind 2 dried wild flowers, 1 dried chamomile, 1 dried yarrow and 1 twine, then
  touch a flame to it. While it smoulders it eases Stress and raises Sanctuary for you anywhere on
  that board, rather than only in your hands, and it burns down over about three hours and is gone.
  Snuff it out to keep the remainder. Appears in Survival > Support. The lit card's art is a
  placeholder. (T2.226)

### Notes

- **The plan's own corrected guidance for the shelf was wrong, and the fix is recorded here because
  the plan row it corrected has now been pruned.** That row told the implementer to filter on
  `tag_Herb` plus `tag_Preservable`. `CardFilter.SupportsCard` treats positive `TagFilters` as OR,
  not AND, so that pair would have accepted every one of the 103 mod cards carrying
  `tag_Preservable` and produced a container that filters nothing. The shipped filter is
  `tag_Herb`, `tag_Fungus`, `tag_Powder`, `tag_Medicine`, which reaches 56 of this mod's cards.
- **Both magnitudes are provisional and have not been balance-tested**: the shelf's 0.6 spoilage
  multiplier, and the incense's -0.3 Stress rate, +20 Sanctuary and 12-unit burn time.
- Truffle Butter and Truffle Salt remain BLOCKED, unchanged from 1.11.0. Their prerequisite is an
  in-game confirmation of the Hemp Butter activation chain, tracked as T2.153, which is still
  unrun.

## [1.11.0] - 2026-09-08

Six new features from the mod's Audit Remediation Plan, plus one silent-failure fix. None of the
below has been confirmed at a running game yet: every item carries a row in
`.claude/playthrough-test-status.json` (T2.188, T2.189, T2.208-T2.211) awaiting human verification.

### Added

- **Three more pressed oils: Yarrow, Chamomile and Ginseng.** The Oil Press went from 8 oils to 11.
  Each presses from the FRESH herb plus a Clay Bowl, matching the four herbal oils already shipped,
  and each carries `tag_Oil` so it joins the lamp-fuel pool. (T2.188)
- **Linseed Oil**, pressed from 3 Flax Seeds plus a Clay Bowl. The Seed Bag perk has always granted
  flax seeds that no recipe in this mod could use; this is their first use. Its description mentions
  the traditional wood-treating use as flavour only: there is no wood-finishing mechanic. (T2.208)
- **Herbalist's Advantage**, a Situational character-creation trait costing 30 Suns that starts you
  with a Herbalism head start. This is the mod's first perk that biases a SKILL rather than granting
  items; all 15 previous perks only handed you objects. (T2.189)
- **Three culinary mushroom seasoning powders**: Black Trumpet, Shiitake and King Oyster. Grind the
  dried mushroom with a mortar and pestle. Until now the mod ground only medicinal species. Each
  powder carries Savoury and Earthy flavour. Art is placeholder. (T2.209)
- **Three mushroom broths with distinct effects**, beside the existing plain Mushroom Broth: Reishi
  (immune and stress), Lion's Mane (focus) and Chanterelle (morale), each brewed from its own dried
  mushroom. All six magnitudes are provisional and have not been balance-tested. (T2.210)
- **Berry Preserve**, the mod's first concentrated-sugar preservation path: four dried berries plus
  honeycomb, slow-simmered into something that keeps far longer than the fruit it came from. (T2.211)

### Fixed

- **Peanut Oil's spice effect never did anything.** The card referenced a spice tag
  (`herbs_fungi_spice_peanut_oil`) that had no backing file, so the reference resolved to nothing and
  the oil silently contributed no flavour or stat effect when cooked with. It was the only one of the
  mod's four spice-tag references that did not resolve. The tag now exists and carries Peanut Oil's
  own flavour profile (Nutty, Earthy, Grassy) plus the same nutrition values its sibling oils use.

## [1.10.18] — 2026-09-05

### Fixed

- **Destroying or harvesting any Pickle Vat (Ready or Sealed, all 4 flavors) returned nothing — the
  vat and its Open Pickle Jar just vanished.** All 8 variant files (`PickleVatReady_{Frogs,Meat,
  Mushrooms,Vegetables}.json`, `PickleVatSealed_{Frogs,Meat,Mushrooms,Vegetables}.json`) authored
  `DroppedOnDestroy` as a flat array of drop entries, but the real field type is a nested collection
  (the same shape `ProducedCards` uses elsewhere in this mod) with the payload one level deeper. The
  mismatched shape deserialized without error, so `DroppedCards` stayed null on every collection and
  nothing spawned — no log, no exception, just an empty destroy. Reshaped all 8 files to the correct
  nested `CollectionName`/`CollectionWeight`/`DroppedCards[]` structure; destroying or emptying a
  Pickle Vat now correctly returns the reusable fired vat plus an Open Pickle Jar, as the README's
  Pickle Vat walkthrough has always described. First identified in the 2026-09-01 audit, fixed here.
  Not yet verified in-game — recommend adding to `/playthrough-test-plan`.

- **"Return Bowl" on an Open Pickle Jar produced no clay bowl, and destroying the jar dropped
  nothing.** The same flat-vs-nested shape bug also affected `CardData/Item/OpenPickleJar.json`, on
  *both* its `DroppedOnDestroy` and its "Return Bowl" `ProducedCards`. It was missed by the sweep
  above because the jar is a CT0 item rather than one of the CT2 vat structure cards, so the
  structure-scoped repair never touched it. With both arrays reshaped to the nested
  `CollectionName`/`CollectionWeight`/`DroppedCards[]` structure, "Return Bowl" now returns the clay
  bowl lid, making the README's "reclaim the clay bowl lid and reduce per-batch clay cost" behavior
  real. Not yet verified in-game.

### Changed

- **Seasonal forage drops are now actually gated by season.** The forage-drop injector's per-item
  season hints ("Summer only", "Spring/Summer/Fall only", "Late Summer/Fall") were previously dead
  parameters — every herb, mushroom, and berry dropped year-round regardless of the comment. Each
  seasonal injected drop now carries a `StatsModifiers` entry keyed on the vanilla
  `SeasonCounter_<season>` GameStats that drives its chance to 0% outside its allowed seasons:
  Blackcurrant and Redcurrant are Summer-only, Lingonberry is Summer/Autumn, and every other
  seasonal herb/mushroom is Spring/Summer/Autumn (absent in Winter). Morels, King/Golden Oyster,
  and dug-for Truffles remain year-round. The suppressor is added only to the mod's own injected
  DropChance — never a vanilla drop — so it is not a hot-path patch. Not yet verified in-game:
  confirm seasonal appearance/absence across a season boundary via `/playthrough-test-plan`.

- **README's Pickle Vat walkthrough now describes the shipped mechanic.** Step 6 said harvesting a
  Ready vat spawns "the pickled goods" as a separate item; there is no such item — the Ready vat
  *is* the food (named for its contents, e.g. "Pickled Frogs") and holds 5 servings eaten straight
  from the vat. Reworded to describe eating servings, with the jar and reusable fired vat returned
  when the vat is emptied or destroyed. Documentation only, no gameplay change.

## [1.10.17] — 2026-09-01

### Fixed

- **Hemp Butter was unreachable — Active Hemp Butter never solidified.** The 2026-09-01 fleet
  feature audit flagged the advertised 3-dose Hemp Butter as dead content. Root cause: Active Hemp
  Butter's "Wetness" stat started at 0 with a negative drain rate, and the engine only fires an
  OnZero action on a positive-to-zero crossing — so the "solidify into Hemp Butter" transform could
  never trigger, stranding the chain one step before the card that carries the three dose actions.
  Wetness now starts full at a retuned 96 DTP (one in-game day) instead of the authored 480: spoilage
  accrued while drying transfers 1:1 into the solid butter (whose spoilage window is 480), so the old
  5-day dry time would have delivered an already-spoiled block even with the stat fixed. Melt fat
  with Hemp Flower Powder, heat to activate, let it rest a day, and the dosed butter now actually
  arrives. Not yet verified in-game.

## [1.10.16] — 2026-08-25

### Fixed

- **Reduced forage-table bloat that a player traced to a performance loss comparable to the village mod.** `AddMushroomDropsToForaging`'s biome matcher had a real double-counting bug: `isClearing` (bare `Contains("Clearing")`) also fired for `ClearingOak`/`ClearingAlder`/`ClearingPine` locations, since those names structurally contain `"Clearing"` — so those three location types received both their specific-biome drop set AND the full generic-clearing drop set (Wild Flowers, Dandelion, Common Plantain, Chamomile, Puffball, Redcurrant, Yarrow, Peanut Pod, all at high chance) stacked on top. Fixed by excluding the three specific-named clearings from the generic bucket.

### Added

- **New `ForageDropDensityScale` config (`[Performance]`, default `1.0`).** Multiplies every H&F-injected forage/dig drop chance. Lower it instead of uninstalling the mod if forage variety still feels too dense after the fix above.

## [1.10.15] — 2026-08-24

### Removed
- **Forest Scout perk removed.** The Overgrown Forest Trail gate it described has applied to every
  character by default since v1.10.7 — the perk itself no longer controlled access to anything, and
  the only thing left to justify its 1-Star cost was a `+1 Foraging Aid` passive bonus (added in
  v1.10.9 as a band-aid once the gate went default). Removing the perk removes that bonus too rather
  than reinventing a hidden always-on replacement for it — the mod now has one fewer redundant
  character-creation choice. Character-creation perk count: 16 → 15.

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
