# Changelog — Sirus23 Mod Collection

All notable changes to this project will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.20.2] — 2026-08-23

### Fixed

- **Finished the spinning-wheel refactor cleanup (feature-honesty).** `Wool.json`'s "Spin Wool", `Yarn.json`'s "Weave Cloth", and `WoolBlanket.json`'s "Sew Tunic" `CardInteraction`s still triggered on a deleted `SpinningWheelLocation` GUID (`e8fc22c8d4de01944b482a3fd8e44efa`) left over from before 1.20.0 moved wool spinning onto its own blueprint chain — silently dead, zero effect if a player somehow still had that GUID on the board. Removed all three, along with their now-orphaned `SimpEn.csv`/`SimpCn.csv` rows, and corrected `Features.json`'s stale "Spinning Wheel" feature-map entry to describe the real blueprint-based chain (`sh_bp_yarn` / `sh_bp_woven_cloth` / `sh_bp_wool_blanket` / `sh_bp_wool_tunic`) instead of the removed vanilla integration. No player-visible change — wool spinning has worked via blueprints since 1.20.0; this only removes dead code the player never saw.
- Removed stale "ships with placeholder white-card art, pending final illustration" README language for the three Prepared Dairy Dishes and the three Felt items (Mittens/Vest/Bedroll) — all six shipped with finished illustrated art in 1.19.0/1.20.0 and the placeholder disclaimer was never updated.

## [1.20.1] — 2026-08-22

### Fixed

- **Wild Owl teleporting into sealed caves/mines.** The untamed Owl's nightly "visit the player"
  duty (`Animals/Owl.json` `Movement.PlayerAttraction`, engine `MoveDutyAction` with
  `MoveDestination: MoveToPlayer` + `MovementType: Teleport`) had no environment-type check at
  all — it always teleported straight to wherever the player currently stood, including caves,
  mines, and man-made structures a bird has no way to enter. Reported: the Owl appeared inside
  AdvancedCopperTools' fully-sealed "Metal Mines". New `WildOwlDutyGuardPatch` postfixes
  `MoveDutyAction.CanBePerformed` to suppress the duty when the player's current environment is
  a known cave/mine/tunnel/structure, falling back to the Owl's next-highest-weight duty instead.
  This is the untamed-agent counterpart to the existing `CompanionStayPatch` cave exclusion,
  which only covered the TAMED Owl companion's follow-along behavior. The shared cave/structure
  UID+tag allowlist was extracted from `CompanionStayPatch` into a new `IndoorOrCaveEnv` helper
  so both patches read from one list.

## [1.20.0] — 2026-08-17

### Fixed

- **CRITICAL — 8 character-creation perks were silently free at character creation instead of
  gated.** `Pk_SH_CraftingKit`, `Pk_SH_HusbandryKit`, `Pk_SH_StartShears`, `Pk_SH_StartSheep`,
  `Pk_SH_ShepherdsStart`, `Perk_FoxFriend`, `Perk_OwlFriend`, and `Perk_WolfFriend` had all reverted
  to `StartUnlocked: true` in an earlier game-version-update commit, undoing a fix that had been
  verified working in-game the same day it was reverted. Restored `StartUnlocked: false` on all 8.
- **Feature honesty — removed the last remnants of the deleted Spinning Wheel.** The structure was
  removed some time ago (spinning is blueprint-only now), but the cleanup was never finished: 3
  permanently-untriggerable `CardInteraction` blocks referencing its deleted CardData GUID (`Wool`
  "Spin Wool", `Yarn` "Weave Cloth", `WoolBlanket` "Sew Tunic") and a dead item grant on `Pk_SH_
  CraftingKit` (silently failed to spawn) are removed; "a spinning wheel" is stripped from
  `ModInfo.json`'s Description, `README.md` (two mentions), and the Crafting Kit perk's description
  (+ both localization CSVs) — none of these ever matched shipped content.
- Added the missing `FC_Owl_Tame_ActionName` localization key to both `SimpEn.csv`/`SimpCn.csv` (the
  M6 tame interaction's "Attempt to Tame" button text had no CSV row).

### Changed

- `ModInfo.json`/`README.md` now mention the wild Owl's M3/M4 mechanics (discoverable tracks, snare
  capture) — shipped since the Animal System's M3/M4 milestones but never previously documented for
  players.

### Added

- `Documentation/Design/Owl_Feature_Map.md` — a 33-row per-interaction trigger→outcome table covering
  the Owl's full presence/tracks/traps/encounters/tame/companion-care surface, the regression anchor
  for the upcoming in-game acceptance pass.
- `Animals/TestHare.json` — a fully-generated, day-active test species (no hand-authored `NPCAgent`,
  `CardData`, C#, or PNG — reuses the vanilla `Hare_Wild` sprite). Undocumented/test-only by design;
  proves the Animal Modding System's manifest-only authoring path on a species other than the Owl.

---

## [1.19.0] — 2026-08-16

### Changed
- **Wild Owl taming is now skill-gated and fully framework-driven (CSFFModFramework 2.25.0, Animal
  System M6).** `Animals/Owl.json` gained `Interactions`/`Companion` sections; the tame roll is
  resolved natively by the engine's own weighted-collection selection instead of always succeeding
  — success chance scales with `Skill_Tracking` (50% at skill 0, 75% at skill 150, TUNABLE), and a
  failed attempt has a 1-in-5 chance to trigger the owl's own combat encounter instead of just doing
  nothing. A failed attempt also makes the owl flee.
- The hand-authored "Attempt to Tame" `DragAndDropAction` in `NPCAgent/Agent_WildOwl.json` is
  removed — the framework's `TameInteractionBuilder` now generates it from the manifest.
  `Patcher/WildOwlLifecyclePatch.cs` is deleted and `Patcher/CompanionHuntPatch.cs`'s owl-specific
  `OwlTameInit` handler is removed; the framework's new `CompanionService` now retires the wild owl
  and initializes the companion's stats on a successful tame. Fox's tame flow (`FoxTameInit`,
  `WildFoxLifecyclePatch.cs`) is unchanged — it has not migrated onto the framework companion
  service yet.

---

## [1.18.0] — 2026-08-16

### Added
- **"Tend Flock" herd action on the Sheep Feeder.** Turns the previously storage-only feeder
  into a husbandry hub: one click shears every ready tame or lactating sheep in the current
  environment, spawning 2x Wool (and a 25% chance of Lanolin) per sheep and resetting its Wool
  stat, exactly matching the individual Shear action's own thresholds and output — not-ready
  sheep are silently skipped. Implemented via `ActionRouter` (`SheepFeederPatch.cs`), the same
  "JSON is just a button hook, Harmony does the real work" pattern as Grind All.
  **Milking is intentionally NOT automated.** Vanilla milk production runs the engine's
  `GameManager.AddCard` coroutine to attach a nested liquid-card instance to a dragged
  container's `ContainedLiquid` slot (confirmed by decompile) — there's no existing precedent
  in this codebase for driving that from C#, and this DA has no container to attach to (no bowl
  is dragged). Automating it would need either a redesigned feeder inventory that also accepts
  water/dairy containers, or a first-of-its-kind `AddCard` reflection path — both bigger design
  calls than this pass. Milking by hand (drag a container onto a Lactating Sheep) is unaffected.

## [1.17.0] — 2026-08-16

### Added
- **Prepared dairy dishes.** Butter, Sheep Cheese, and Cream now each have a cooked-dish payoff
  beyond the eat button, via a new drag-onto-vanilla-food `CardInteraction` on each dairy item
  (no vanilla file was modified — a `CookingRecipe` station-array injection was considered but
  ruled out: the framework has no declarative way to append an embedded `CookingRecipe` entry to
  a station's `CookingRecipes` array without either full C# or risking an unintended overwrite
  of the station's existing vanilla recipes, so a drag-to-transform `CardInteraction` on the
  mod's own dairy item — the same proven pattern as `CMC_OldGrowthBark`'s "Tan Hide" and this
  mod's own Butter "Clarify" CI — was used instead):
  - Drag **Butter** onto a vanilla **Roasted Cattail Root** → **Buttered Roots** (`sh_buttered_roots`).
  - Drag **Sheep Cheese** onto a vanilla **Wheat Roundbread** → **Cheese-Stuffed Flatbread**
    (`sh_cheese_stuffed_flatbread`).
  - Drag **Cream** onto a vanilla **Mashed Turnroot** → **Creamy Mash** (`sh_creamy_mash`).
  - Each dish is a plain CT0 food item (`EdibleStats` + an explicit `Eat` `DismantleAction`,
    since `EdibleStats` alone renders no eat button) with stronger nutrition than either raw
    ingredient. The original plan's third example was "Creamy Stew," but vanilla has no CT0
    solid "stew" dish to serve as a transform target (stew is a liquid/`CookingRecipe`
    mechanic) — substituted with Mashed Turnroot + Cream ("Creamy Mash") instead.
  - **Nutrition values, trading values, and ingredient pairings are initial balance guesses,
    not final-tuned numbers.** All three ship with placeholder white-card art pending final
    illustration.
- **Owl trap catching (CSFFModFramework Animal M4 test vehicle).** The Owl companion can now be
  caught with a vanilla Snare (`Traps` block on `Animals/Owl.json`, framework's declarative
  `TrapIntegrator` — no mod-side C#): 70% caught alive as `wildowl_tied` (Snared Owl, must be fed
  or it works itself free; can be released or killed for its carcass), 30% as `wildowl_carcass`.
  Owl is immune to Deadfall/Log/Pit traps (`Wariness: 25`, those three types set to a 10000
  effective-weight penalty). Placeholder art on the new `wildowl_tied` card, pending final
  illustration. **Known gap:** feeding the trapped owl consumes bait but does not yet reset its
  hunger stat — the framework's `SimpleCardChange` path exposes no NPC-stat hook for that; deferred.

## [1.16.1] — 2026-08-16

### Fixed
- **Sheep Pen art.** The Sheep Pen blueprint, placed structure, and portable item all referenced
  `sh_sheep_pen`, a shipped-but-blank placeholder PNG — every Sheep Pen card rendered with no image.
  Swapped all three to the vanilla `Enclosure_Door` sprite (the same sprite vanilla's own
  `EnclosureEntrance` structure uses for its livestock-pen entrance) and deleted the now-unused
  placeholder PNG.

## [1.16.0] — 2026-08-16

### Added
- **Sheep Pen** (`sh_sheep_pen`, blueprint `sh_bp_sheep_pen`, 8 Plank + 4 Rope + 4 Stone, registered
  under Farming › Animal Husbandry alongside the Sheep Feeder) — a buildable enclosure holding up to 4
  tame sheep/rams (`InventoryFilter` accepts both `tag_Sheep` and `tag_Ram`, since tame rams carry both
  tags). Confirmed via decompile that a plain container works: tame sheep already carry
  `AlwaysUpdate: true`, and that flag keeps applying regardless of container nesting, so wool regrowth,
  milk, and ram-proximity breeding continue normally while penned. The pen ships
  `SpillsInventoryOnDestroy: true` so using its "Pick Up" action while sheep are inside releases them
  onto the ground first instead of destroying them (the vanilla default for a container with no spill
  flag set is to recursively delete its contents on pickup — this was caught and fixed before shipping,
  not inherited from the Sheep Feeder chassis it's cloned from).
- **Pen-or-perish night escape/predation.** Every in-game night, a tame sheep, ram, or lactating sheep
  left OUTSIDE a pen (in the player's current environment) risks wandering off or being taken by a
  predator overnight; a predator kill leaves a new **Sheep Remains** card (`sh_sheep_remains`) behind so
  the loss is always explainable, never a silent disappearance. A **Wolf Companion** present in that
  environment suppresses the roll entirely that night (mirrors the wolf's existing wildlife-suppression
  presence check). Penned sheep are always exempt — checked before any roll fires.
  **The escape (4%) and predation (6%) per-sheep nightly chances are conservative starting
  placeholders, not balanced/final values** — flagged for playtest feedback. `Patcher/SheepPenPatch.cs`.
- Known scope limit: the nightly check only evaluates the environment the player currently occupies —
  a flock left at a distant, unvisited environment is not rolled against until the player returns to it.
  `CardData/Location/SheepPen.json`, `CardData/Item/SheepPen_Portable.json`,
  `CardData/Item/SheepRemains.json`, `CardData/Blueprint/Bp_SheepPen.json`. Ships with placeholder
  white-card art (`sh_sheep_pen.png` / `sh_sheep_remains.png`) pending final illustration.

## [1.15.0] — 2026-08-16

### Added
- **Wild Fox tame path.** A Wild Fox can now be encountered in the wild (daytime, 6:00–20:00) and
  tamed by offering it dried berries — a fresh character without the **Fox Friend** perk can obtain
  the same Fox Companion this way. Approaching the fox opens a brief encounter (flee or minor
  scuffle) before the tame drag-action becomes available. Built on the Animal Modding System's
  Ref-path manifest (`Animals/Fox.json` + hand-authored `NPCAgent/Agent_WildFox.json` +
  `Encounter/Encounter_WildFox.json`), mirroring the already-shipped Wild Owl chain — the framework's
  `AnimalLifecycleTicker` owns the fox's death/respawn lifecycle; `Patcher/WildFoxLifecyclePatch.cs`
  + a new `CompanionHuntPatch` handler only supply the tame-flow NPC retirement glue the framework
  doesn't cover yet. Wildlife encounters are partially suppressed while a tamed Fox Companion is on
  the board (`EncounterGuards/FoxGuard.json`, 35% chance). The equivalent Wild Wolf tame path is a
  planned follow-up, not included here — the Wolf remains perk-only. Ships with a placeholder white
  `WildFox.png` pending final illustration.

## [1.14.0] — 2026-08-16

### Added
- **Felt Mittens** (`sh_felt_mittens`, hands, +5 Cold Resistance) and **Felt Vest** (`sh_felt_vest`,
  outer torso, +12 Cold Resistance) extend the felt-working line alongside the existing Felt Hat and
  Felt Boots. Both craft from 2 Felted Wool via new blueprints (`sh_bp_felt_mittens`,
  `sh_bp_felt_vest`) registered in the Cloth subtab of the Tailoring tab. The Felt Vest occupies the
  same `eTag_OuterTorso` slot as the Wool Tunic (one outer-torso garment at a time), matching that
  item's existing slot choice. Both items ship with placeholder white-card art pending final
  illustration, and their Cold Resistance values are an initial balance guess, not final-tuned
  numbers. `CardData/Item/FeltMittens.json`, `CardData/Item/FeltVest.json`,
  `CardData/Blueprint/Bp_FeltMittens.json`, `CardData/Blueprint/Bp_FeltVest.json`.
- **Felt Bedroll** (`sh_felt_bedroll`) closes the gap between the README's long-standing "wool
  clothing and bedding" claim and the mod actually shipping only one bedding item (Wool Blanket).
  Felts 3 Felted Wool into a bedding item that is a distinctly stronger upgrade over the Wool
  Blanket rather than a reskin: +16 to the same sleep-insulation stat the blanket boosts
  (`888d2d2a99e3f044291c6748a0fa8d78`, vs. the blanket's +10), at 900g vs. the blanket's 700g. New
  blueprint `sh_bp_felt_bedroll` is registered in the Cloth subtab of the Tailoring tab. Ships with
  placeholder art pending final illustration; the stat bonus is an initial balance guess against the
  Wool Blanket, not a final-tuned number. `CardData/Item/FeltBedroll.json`,
  `CardData/Blueprint/Bp_FeltBedroll.json`.

## [1.13.2] — 2026-08-15

### Fixed
- **All 8 character-creation perks (Wolf Friend, Fox Friend, Owl Friend, Shepherd's Start, Sheep
  Husbandry Kit, Shepherd's Crafting Kit, Start with Shears, Start with Sheep) were unselectable at
  character creation.** An unintended edit set `StartUnlocked: false` on all eight, which turns a
  perk from a free starter pick into one gated behind earned Suns/Moons meta-currency — on a fresh
  profile (0 Suns/Moons) none were affordable, so none of the mod's companion or husbandry content
  was reachable at character creation. Reverted all eight to `StartUnlocked: true`, restoring the
  free-starter-perk behavior described in this mod's README. `CharacterPerk/*.json`.

## [1.13.1] — 2026-08-12

### Fixed
- **Owl companion still followed the player into caves and man-made structures despite the 1.5.7
  fix.** Root cause: that fix's indoor/cave detection relied entirely on the destination
  environment's `CardTags` (`tag_Cave`/`tag_EnvCaveSystem`/`tag_EnvIndoors`), but this proved
  unreliable — Community_Mod_Chest's 7 building interiors (Inn, Academy, both cottages, Village
  Hall, Apothecary's Cabin, Jail Cell) shipped with **zero** `CardTags` at all, and vanilla cave
  data can't be independently verified offline (obfuscated tag names in the static JSON export).
  The Owl-follow check now checks the destination's UniqueID against a curated list of every
  known cave/mine/tunnel and man-made structure across the vanilla game and this mod fleet (30
  vanilla caves/mines, 16 vanilla home-construction environments, 7 Community_Mod_Chest building
  interiors, 5 AdvancedCopperTools clone caves) as the primary signal, with the CardTags scan kept
  as a secondary check for content not on that list. `Patcher/CompanionStayPatch.cs`.

## [1.5.7] — 2026-08-09

### Fixed
- **Owl companion no longer follows the player into caves or structures.** It has `AlwaysUpdate`
  follow behavior like Wolf/Fox, so it was tagging along through any environment transition —
  including a Portal Hub teleport straight into a modded mining cave, bypassing a "collapsed
  tunnel" obstruction meant to gate that passage. The Owl is now left behind (saved into the
  current outdoor environment, same mechanism as the existing "Stay Here" action) whenever the
  destination carries an indoor/cave biome tag (`tag_Cave`, `tag_EnvCaveSystem`, `tag_EnvIndoors`).
  Wolf and Fox are unaffected. `Patcher/CompanionStayPatch.cs`.

## [1.5.6] — 2026-08-07

### Added
- **Full Chinese localization.** Created `Localization/SimpCn.csv` covering all 269 player-visible
  strings — the 30 already-translated entries that had been stranded in `SimpEn.csv`'s unread third
  column (Chinese mode never reads `SimpEn.csv`) were carried over, and the remaining ~239 keys
  (companion animal dialog/actions, Sheep Husbandry items/blueprints/perks, felt-working chain) were
  translated. Fixes the "some parts are still missing" Chinese-text gap reported for this mod.

### Fixed
- **4 `SimpEn.csv` rows had unquoted internal commas, silently truncating their in-game English text**
  at the first comma (`FC_WolfCompanion_CardDescription`, `FC_WolfCompanion_DA2_ActionDescription`,
  `sh_butter_churn_portable_CardDescription`, `sh_sheep_feeder_CardDescription`). Wrapped the affected
  fields in double quotes; no wording changed.

## [1.5.5] — 2026-08-07

### Added
- **13 perishable dairy items and Sheep Milk now carry `tag_Preservable`** (Butter, Buttermilk,
  Clarified Butter's precursors, Cream, Curdled Milk, Ricotta, Sheep Cheese, Aged Sheep Cheese, Sour
  Cream, Warm Milk, Whey, Yogurt, Owl Carcass, Sheep Milk). This is a vanilla CardTag with no shipped
  vanilla users; third-party mods that bulk-match spoilage-rate effects onto it (e.g. freshness/
  preservation perk mods) now apply correctly to our dairy chain. Deliberately excluded: Lamb (its
  `SpoilageTime` channel is relabeled "Growth", not spoilage) and the Owl/Fox/Wolf Companions (relabeled
  "Hunger") — tagging those would apply a food-freshness effect to pet hunger/growth rates, which is a
  different mechanic wearing the same durability channel.

## [1.5.4] — 2026-08-02

### Fixed
- **Companion animals (Wolf, Fox, Owl) never auto-drank from a Rain Cistern — or any water container.** The auto-drink upkeep read each source's own `CurrentLiquidQuantity`, but containers (Rain Cistern, Clay Basin, Watering Trough, Clay Bowl/Jar, Copper Bottle/Jar, Waterskin, and the modded vessels) store their water in a separate `ContainedLiquid` child card — so every container reported 0 water and was skipped, and a thirsty companion sat next to a full cistern without drinking. The upkeep now resolves the container's `ContainedLiquid` when reading the available water and when draining it, matching how the game's own `GetCurrentDurability(Liquid)` routes. Water sources that are themselves a Liquid card are unaffected.
- **Lactating Sheep could be sheared infinitely, the same bug 1.5.2 fixed for Tame Sheep.** The Shear CardInteraction's Wool gate (`RequiredSpecial1Percent`) was authored under `RequiredReceivingContainerDurabilities` (checks the sheep's *container*, always empty on the base board — a permanent no-op) instead of `RequiredReceivingDurabilities` (checks the sheep card itself). Moved the gate to the correct field; Shear now re-locks until Wool regenerates, matching the intended once-per-regrowth-cycle design.
- **Lactating Sheep's Lanolin never dropped from Shear at the intended rate.** `DropChance.BaseDropChance` was set to `0.25` — vanilla `DropChance.BaseDropChance` is a 0–100 percent scale, not a 0–1 fraction, so the real chance was 0.25% (1-in-400) instead of the intended 25%. Corrected to `25.0`, matching the 1.5.2 fix already applied to Tame Sheep.
- **Sheep hides (`SkinFresh`) dropped from Slaughter with zero durability stats.** `Lamb`, `Male Sheep`, `Tame Sheep`, `Wild Male Sheep`, `Wild Sheep`, and `Lactating Sheep`'s Slaughter action now sets the produced hide's stats via `TransferRules` at spawn time instead of leaving them at the default zero.

### Changed
- **Tame Sheep's Shear gate now unlocks at 50% Wool regrowth instead of 99%.** Lowered `RequiredReceivingDurabilities.RequiredSpecial1Percent` from `0.99` to `0.5` on both Tame Sheep and Lactating Sheep — shearing is available sooner into the regrowth cycle.

## [1.5.3] — 2026-07-27

### Changed
- **30 tradeable items now have NPC trading values** (previously 0 = free at trading tables).
  Dairy follows vanilla's scale (milk 15, butter 45, sheep cheese 150, aged sheep cheese 400);
  wool textiles anchor to vanilla cloth (wool 40, woven cloth 300, wool tunic 900, felt boots
  500); tools/equipment (Shears 400, Butter Churn 250); livestock anchors to vanilla animals
  (Lamb 800, Ram 2500, Tame Sheep 3000, Lactating Sheep 3500). Wild Sheep and Wild Ram
  intentionally stay 0 — wildlife, not market goods. Part of the fleet-wide trading reprice.

## [1.5.2] — 2026-07-22

### Fixed

- **Tame Sheep could be sheared infinitely.** The Shear CardInteraction's 99%-Wool gate
  (`RequiredSpecial1Percent`) was authored under `RequiredReceivingContainerDurabilities`
  (checks the sheep's *container*, which is always empty on the base board — the gate was a
  permanent no-op) instead of `RequiredReceivingDurabilities` (checks the sheep card itself).
  Moved the gate to the correct field; Shear now re-locks until Wool regenerates (~480 DTP)
  after each shearing, matching the intended once-per-regrowth-cycle design.
- **Lanolin never dropped from Shear.** `BaseDropChance` was set to `0.25` — vanilla
  `DropChance.BaseDropChance` is a 0–100 percent scale, not a 0–1 fraction, so the real chance
  was 0.25% (1-in-400) instead of the intended 25%. Corrected to `25.0`.

## [1.5.1] — 2026-07-17

### Fixed

- **Clothing warmth applied while unequipped and stacked per copy.** Felt Hat, Felt Boots, Wool Socks,
  and Wool Tunic now set `AffectStatsOnlyWhenEquipped: true` (matching vanilla convention for every
  equippable item with a passive stat effect), so their Cold Resistance bonus only applies while worn
  in the correct equipment slot.
- **Woven Cloth no longer warms the player passively.** The raw crafting material carried an unintended
  always-on Body Temperature bonus; removed.
- **Wild Ram bred without being tamed first.** `Wild Ram` no longer carries the Ram tag that the
  sheep-husbandry breeding passive checks for — it must be tamed into a `Ram` first, matching the
  intended design.

---

## [1.5.0] — 2026-07-16

### Added

#### Companion "Stay Here" / "Follow Me"
- Wolf, Fox, and Owl companions each gain two new self-actions: **Stay Here** (companion stays behind in its current environment instead of following you) and **Follow Me** (resumes following). Only one of the two is ever shown, depending on current state.
- `CompanionStayPatch.cs` — postfixes `InGameCardBase.IndependentFromEnv` so a "staying" companion is treated like any other environment-bound card on the next environment change, instead of riding along via `AlwaysUpdate`.
- Backed by a hidden per-instance flag (`SpecialDurability2`, always hidden from the UI) so the state persists across saves.

## [1.3.10] — 2026-07-12

### Added

#### Wild Owl
- **Wild Owl** — a nocturnal forest creature powered by the animal system. Spawns on the world board and roams the forest. Offer a fish to begin taming, or leave it alone and it will wander away over time.
- **Owl Companion** — a tamed Wild Owl. Runs the **Night Hunt** duty at night (guards the camp and hunts prey). Returns in the morning. On death drops an **Owl Carcass**.
- **Owl Carcass** — new item; process to obtain owl feathers.
- Animal system data: `Agent_WildOwl.json`, `Encounter_WildOwl.json`, five NPC stats (`Exists`, `Blood`, `Wildness`, `RespawnTimer`, `CompanionRespawn`), `Duty_WildOwl_NightHunt`, `WildOwl_MoveToPlayer` move-duty action.
- `WildOwlLifecyclePatch.cs` — handles spawning, taming sequence, companion registration, and respawn timer.
- New artwork: `WildOwl.png`, `OwlCarcass.png`.

#### Transfer guard
- `CompanionContainerGuardPatch.cs` — prevents any companion card (wolf, fox, owl) from being moved into a container. The vanilla `CannotBeTransferred` flag only guards the drag-and-drop path; this patch enforces the rule on all programmatic transfer paths as well.

### Changed

- `Bp_Yarn.json` renamed to `Bp_SHYarn.json` to prevent UniqueID conflicts with other mods that may ship a `Bp_Yarn` blueprint.
- `WolfTickPatch.cs` updated — morale/hunger/thirst stats initialised on spawn so the companion is not immediately removed by the tick handler on the first DTP tick after taming.
- Existing sheep, lamb, and fox companion cards updated with minor data fixes.

### Fixed

- Wolf (and all companions) can no longer be dropped into containers by the player — enforced by the new `CompanionContainerGuardPatch`.

---

## [1.3.2] — 2026-07-07

### Added

- **Lanolin Salve** (`sh_lanolin_salve`) — the shearing byproduct finally has a use. Blend 1 Lanolin + 1 Frostleaf Powder (blueprint `sh_bp_lanolin_salve`, Survival › Medical subtab, discovered when Lanolin is on the board, 2h research) into a soothing salve. **Apply to Skin** (15 min) consumes the salve and restores +15 Comfort. Uses the vanilla `Salve_Soothing` card art.

---

## [1.3.0] — 2026-06-21

### Added

#### Sheep Husbandry

**Animals**
- **Wild Sheep** — roams the forest; offer fresh meadowgrass to tame it or slaughter for resources
- **Wild Ram** — offer fresh meadowgrass to tame or slaughter; spawns occasionally on the board
- **Tame Sheep** — shear for wool, keep near a ram to breed over time, or slaughter
- **Lactating Sheep** — recently gave birth; milk daily with a clay bowl, shear for wool, or slaughter
- **Ram** — keep near tame sheep to impregnate them over time, or slaughter for resources
- **Lamb** — grows into a tame sheep or ram by end of fall; feed meadowgrass or straw

**Dairy**
- **Sheep Milk** (liquid) — fresh sheep milk; base ingredient for the entire dairy chain
- **Warm Milk** — comforting and nutritious
- **Cream** — rich cream skimmed from fresh sheep milk; fatty and filling
- **Butter** — churned sheep butter; drag a fire source to clarify into pure fat
- **Buttermilk** — tangy byproduct of churning butter
- **Clarified Butter** — pure golden fat; extremely calorie-dense and long-lasting
- **Sour Cream** — tangy cultured cream; rich and filling
- **Curdled Milk** — press over a fire to make cheese and whey
- **Sheep Cheese** — fresh cheese; leave out to age; spoilage bar fills as it ages
- **Aged Sheep Cheese** — richly flavoured aged cheese; excellent nutrition
- **Ricotta** — soft ricotta made from reheated whey
- **Yogurt** — tangy fermented sheep milk; more nutritious and hydrating than fresh milk
- **Whey** — liquid byproduct of cheese making; mildly nutritious
- **Lanolin** — waxy byproduct of shearing

**Wool & Textile**
- **Wool** — raw wool sheared from a sheep; spin into yarn
- **Yarn** — spun wool yarn; weave into cloth
- **Woven Cloth** — woollen cloth; provides a small amount of warmth; craft into clothing or blankets
- **Felted Wool** — compressed wool fibres; can be cut and shaped without weaving
- **Cheese Cloth** — fine cloth used to curdle milk into cheese

**Clothing**
- **Wool Blanket** — greatly improves sleep quality in cold weather
- **Wool Tunic** — warm woollen tunic with excellent insulation
- **Wool Socks** — worn beneath boots to keep feet dry and warm
- **Felt Hat** — thick felt hat; keeps the head warm in cold weather
- **Felt Boots** — dense felt boots; insulate against cold ground

**Tools & Structures**
- **Shears** — metal shears for shearing sheep; lasts for many uses
- **Butter Churn** (portable kit + placed station) — churn milk into butter and buttermilk
- **Sheep Feeder** (portable kit + placed structure) — wooden trough for storing meadowgrass or straw for sheep

**Blueprints**
- Shears, Butter Churn, Cheese Cloth, Yarn, Woven Cloth, Felted Wool, Wool Blanket, Wool Tunic, Wool Socks, Felt Hat, Felt Boots, Sheep Feeder

**Spawn Triggers**
- Wild sheep and wild rams occasionally spawn on the board during the run

---

#### Animal Companions

- **Wolf Companion** — loyal wolf that follows you; keep it fed and watered; can be sent on hunts
- **Fox Companion** — quick-witted fox that has taken a liking to you; keep it fed and watered
- **Owl Companion** — silent owl that perches nearby and watches over camp at night; keep it fed and watered

---

#### Perks

**Sheep Husbandry**
- **Shepherd's Start** — begin with two tame sheep, a ram, a lactating ewe, and shears
- **Sheep Husbandry Kit** — begin with a full breeding flock: a ram, a lactating ewe, and a lamb
- **Shepherd's Crafting Kit** — begin with wool, warm milk, and all processing equipment (spinning wheel, butter churn, cheese cloth)
- **Start with Sheep** — begin with a tame sheep; pairs well with Start with Shears
- **Start with Shears** — begin with sheep shears; pairs well with Start with Sheep

**Animal Companions**
- **Wolf Friend** — start with a loyal wolf companion
- **Fox Friend** — start with a curious fox companion
- **Owl Friend** — start with a wise owl companion
- **Pack Bond** — your bond with the wolf runs deep; heightened instincts sharpen your tracking
