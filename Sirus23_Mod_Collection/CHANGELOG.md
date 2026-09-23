# Changelog — Sirus23 Mod Collection

All notable changes to this project will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.21.6] - 2026-09-22

### Fixed

- **Cheese Cloth, Felt Mittens and the Ram were see-through in places.** Cutting the 1.21.5 art off
  its paper backing leaked through a gap in each picture's ink outline and ate away the pale parts
  inside it, so the board showed through the middle of the cloth, down the face of both mittens, and
  across the ram's shoulder. All three are solid again, with their painted weave, felt and fleece
  intact, and the gaps that are meant to be see-through (the shears' handles, the curl of the ram's
  horn, between a sheep's legs) are untouched.

---

## [1.21.5] - 2026-09-18

### Changed

- **New art for the dairy and wool cards and the sheep.** Cream, Curdled Milk, Wool, Yarn, Felted
  Wool, Woven Cloth, Wool Blanket and Wool Tunic have new paintings that show what each one is: a
  squat pot of cream with a drip from its pinched lip, white curds floating in pale whey, crimped
  wool locks, a two-ply ball of yarn, felt folded once, a blanket with two russet stripes and a
  fringe. Whey and Lanolin were borrowing the game's Old Milk and Fat pictures and now have their
  own. The Lamb, Ram, Tame Sheep and Lactating Sheep were painted standing in a meadow, unlike
  every other card; they are now the same animals on a plain card, and the Lactating Sheep, whose
  picture was an exact copy of the Tame Sheep's, now shows its udder. The Owl Carcass is now a dead
  owl lying on its side. The Cheese Cloth blueprint was still showing the old Woven Cloth picture
  and now shows the cheese cloth.

## [1.21.4] - 2026-09-18

### Changed

- **Finished art for fifteen cards that were showing a placeholder.** Ten items were plain white
  cards, as were the blueprints for four of them: Berry Mix, Berry Yogurt, Bone Treat, Buttermilk
  Hardtack, Feather Pillow, Owl Treat, Ricotta Rye Round, Salted Butter, Smoked Cheese and Soured
  Turnroot. Five more borrowed another item's picture: Clarified Butter showed Butter's, Sour Cream,
  Warm Milk and Yogurt all showed Cream's, and Cheese Cloth showed Woven Cloth's. Each now has its
  own illustration. Art only, no gameplay change.

### Fixed

- **Buttermilk showed no picture at all.** Its image pointed at `ClayBowl`, a sprite name the game
  does not have, so the card rendered blank. It now has its own illustration.

- **Felt Mittens sat on a white rectangle.** Its picture still carried the white paper it was
  painted on, where every other card is cut out cleanly; it now matches them. The sixteen new
  pictures above also lose a faint paper texture that was left around their edges, and the
  download is a little smaller for it. Art only, no gameplay change.

## [1.21.3] - 2026-09-17

### Changed

- **Smaller download (now 16.2 MB).** The build script wrote a second, byte-identical copy of every
  card image into `Resource/Texture2D/` alongside `Resource/Picture/`, which nothing ever read, so
  every release archive carried the whole art set twice. The Felt Mittens image had also shipped at
  1696x2528 instead of the 512-wide size the rest of the mod uses. Both fixed; no gameplay change.

- **Every dairy food in this mod now adds Dairy Saturation, the way vanilla milk and cheese do.**
  Eating or drinking a Sirus23 dairy product used to leave that stat untouched, so nothing in the
  game could tell a Sheep Cheese from a Turnroot. This is what lets Community Mod Chest's Lactose
  Intolerant trait react to our dairy the same way it reacts to vanilla's: Sheep Cheese, Aged Sheep
  Cheese and Smoked Cheese add 7.5, Ricotta adds 3.75, Warm Milk, Yogurt and a mug of Sheep Milk add
  4.5, Butter, Salted Butter and Clarified Butter add 18, Cream and Sour Cream add 10.5, Whey adds
  0.75 and Buttermilk adds 2.25, matching vanilla's own cheese, milk and butter amounts. The mixed
  dairy dishes add half of their main dairy ingredient's amount: Berry Yogurt 2.25, Buttered Roots 9,
  Buttermilk Hardtack 1.125, Cheese-Stuffed Flatbread 3.75, Creamy Mash 5.25, Ricotta Rye Round 1.875
  and Soured Turnroot 5.25. Nothing about spoilage, nutrition or trading value changes.

## [1.21.2] - 2026-09-09

### Changed

- **A Wolf Companion now reduces the pen-or-perish predation roll instead of cancelling it.** With
  a wolf on the board, each unpenned sheep or ram rolls at 1% predation per night (6% without a
  wolf); the 4% wandering-off roll is unchanged, and penned sheep stay exempt. Until now the wolf's
  presence skipped the roll entirely, so for anyone playing with the wolf (one of this mod's own
  headline features) the Sheep Pen had no consequence to prevent: playthrough r33 logged five
  nights of "stood guard" with zero losses and the tester recorded "Sheep do not die" (tracker
  T2.101). The nightly log line still says the wolf stood guard and now states the odds it rolled
  at, and a loss under guard is logged as such. Both percentages remain balance placeholders, not
  final-tuned values. NOT yet confirmed in-game: the SheepRemains drop on a predator kill is still
  untested and needs a night without a wolf.

## [1.21.1] - 2026-09-09

### Changed

- **Wild fox tame now accepts fresh berries.** The "Attempt to Tame" drag on a wild fox (the
  GameSourceModify patch on vanilla `Agent_Fox1` / `Agent_Fox2`) only accepted DRIED Springberries,
  Bilberries and Juniper Berries. Fresh berries, which a player is far more likely to be holding when
  a fox turns up, were silently refused: a drag the trigger rejects greys the card out with no action
  name, no tooltip and no message. All three fresh berries now work as bait, and the action text says
  "fresh or dried". This is the prime suspect behind the r33 report "vanilla foxes do not respond to
  berry gifts" (tracker T2.99) and is NOT yet verified in-game; T2.99 stays `fail` until a tester
  drags berries onto a wild fox with this version deployed. The second candidate cause (whether the
  patch lands on the fox agent at all) is answered by the `GameSourceModify: [Sirus23 Mod
  Collection] patched Agent_Fox1` line, which needs one launch with `VerboseLogging = true` in the
  framework cfg; that flag is armed but the game had not been run since it was set. The Fox
  Companion's own "Give Treat" bond action is unchanged and still wants dried berries.

## [1.21.0] - 2026-09-07

### Added

- **Salvage the Sheep Remains.** A sheep lost to a predator overnight leaves a `Sheep Remains` card
  that, until now, could only rot away. Dragging any cutting tool onto it now offers **Salvage
  Remains** (3 DTP), returning 2x Wool, 1x Fresh Hide, 2x Raw Meat and 1x Bones. A lost sheep is now
  a partial recovery instead of a dead-end card.
- **Dairy preservation branch.** Every dairy product in the chain used to spoil on the same short
  clock with no way to extend it. Two preservation steps now exist: drag **Salt** onto Butter for
  **Salted Butter** (3x the shelf life of fresh butter), and drag any fire source onto Sheep Cheese
  to **Smoke the Cheese** for **Smoked Cheese** (4x the shelf life of fresh cheese, and the mod's
  highest-value dairy product).
- **Cheese cloth gains a second use.** The cheese cloth only ever participated in the Curdle step.
  Dragging it onto Curdled Milk now offers **Strain Curds** (3 DTP), a fire-free way to press cheese
  by hand. It costs more time than "Press Cheese" and the whey runs off and is lost, so the fire
  route stays the better one when you have a fire.
- **Cooked-dish payoff for the cultured dairy line.** Yogurt, Sour Cream, Ricotta and Buttermilk were
  all eat-raw terminals with no second use. Each now has a drag-onto-vanilla-food dish, matching the
  pattern the 1.17.0 Butter/Cream/Sheep Cheese dishes already use: Yogurt onto Dried Springberries
  for **Berry Yogurt**, Sour Cream onto Boiled Turnroot for **Soured Turnroot**, Ricotta onto a Rye
  Roundbread for a **Ricotta Rye Round**, and Buttermilk onto Wheat Hardtack for **Buttermilk
  Hardtack**. Every one of the mod's 11 dairy products now has a use past the eat button.
- **Feather Pillow** (blueprint under Tailoring > Cloth: 1 Woven Cloth + 12 Feathers + 4 Twine).
  Processing an owl carcass yields 6 Feathers and nothing in the mod consumed them, so they piled up
  with no sink. The pillow is a bedding item granting +10 Comfort while carried.
- **Craftable companion treats.** Three cheap crafted foods, one per companion, each giving twice the
  bond a raw treat does (+300 vs. +150) plus a small Morale gain and Loneliness relief for the player:
  **Bone Treat** (1 Bones + 1 Dried Meat) for the wolf, **Owl Treat** (1 Raw Meat + 1 Salt) for the
  owl, and **Berry Mix** (1 Dried Springberries + 1 Dried Bilberries) for the fox. All three
  blueprints sit under Survival > Cooking. The companions' existing raw-food "Give Treat" actions are
  unchanged; these are an additional, deliberate bonding act rather than a replacement.

### Notes

- **All nutrition, shelf-life, bond and Comfort values in this release are initial balance guesses,
  not tuned numbers.** In particular the treats' +300 bond and the preservation multipliers are
  placeholders chosen to be clearly better than the raw alternative, and no more than that.
- The ten new cards ship with **placeholder white card art**. The JSON already points at the final
  filenames, so real art drops in over the same names with no JSON change.
- None of this release has been verified in a running game; the acceptance checks are filed in
  `.claude/playthrough-test-status.json` (T2.201 through T2.206).

## [1.20.7] - 2026-09-07

### Fixed

- **The wild owl's night attack could never fire.** `Animals/Owl.json` set `Encounter.Aggression.BaseWeight`
  to 600 while `Movement.PlayerAttraction.BaseWeight` is 1e9, and duty selection is winner-take-all
  rather than weighted-random: `InGameNPC.SelectDuty` sorts eligible duties by weight descending and
  expands its random-pick group only while the next weight is EQUAL to the top one. The attack window
  (22-04) sits entirely inside the owl's activity window (20-06), the seek-player duty carries no
  per-day cap, and `MoveDutyAction.CanBePerformed` is unconditionally true for Teleport, so the seek
  duty won every single tick the attack duty was eligible. The attack duty existed, was attached, and
  never once ran. Present since the M5 conversion (framework 2.25.0).
- The weight is now equal to `PlayerAttraction.BaseWeight` (1e9), the same fix the M4 pass applied to
  `Traps.Bait.DutyWeight` for the same reason: seek, bait-raid and attack all tie at the top and the
  owl picks uniformly among them, with `Aggression.MaxPerDay: 1` capping it at one attack per night.
  Those three weights must now be retuned together, and framework 2.25.28's validator enforces it.
- **Player-visible:** a wild owl at night can now actually attack, up to once per night, where before
  it could only be fought by pressing Approach. Not yet playtested (tracker T2.176).

## [1.20.6] — 2026-09-06

### Removed

- **`Animals/TestHare.json`** — the undocumented "Test Hare" test species added in v1.20.0 no longer
  ships. It was authored purely as an internal proof that the Animal Modding System can produce a
  working wild animal from one manifest with no mod C# and no new art, and was deliberately left out
  of `ModInfo.json`/`README.md` — but nothing gated it, so the framework loaded it into real games
  and players could find a card literally named "Test Hare" wandering the Oak groves. Its
  `Approach` button was worse: the manifest declared no `Encounter` section, so the framework's
  last-resort fallback wired it to the vanilla `Combat_EncounterDuck`, meaning approaching the hare
  opened a duck fight with the duck's own portrait and body template. The game already has a real
  vanilla hare, so this is a straight removal rather than a promotion to documented content. Nothing
  else in the mod referenced `sirus_test_hare` — no localization rows, no C#, no card or perk — so
  no other behavior changes. Existing saves are unaffected beyond the hare ceasing to spawn.
  (Closes audit finding **M4**.)

## [1.20.5] — 2026-09-02

### Fixed

- **Sheep Milk now keeps spoiling while you are away** (`AlwaysUpdate: true` on the CT9 liquid).
  Game patch EA 0.67e fixed the same bug for every live vanilla spoilable liquid ("Rye Flour and
  other bulky powders not properly updating their spoilage when stored in bulk in a container
  while the player is away") - Sheep Milk was the only mod liquid still missing the flag, so milk
  stored in a container effectively stopped spoiling whenever its location was off-screen.

## [1.20.4] — 2026-09-01

### Fixed

- **Restored the Fox Companion's ambient-encounter suppression** (`EncounterGuards/FoxGuard.json`:
  35% chance to suppress a wildlife/ambient encounter while a tamed Fox Companion is on the board,
  advertised since 1.15.0). The 1.20.3 fox rework deleted this file along with the duplicated
  wild-fox species scaffolding, but the guard was keyed on `fc_fox_companion` - the tame companion
  card, which the rework kept - so the deletion silently regressed a live companion feature rather
  than removing dead wild-fox data. Builds 1.20.3 (2026-08-25) through this fix shipped without the
  suppression. Restored verbatim; the README's Fox Companion section now also documents the
  suppression (it never did, unlike the Wolf/Owl sections).

## [1.20.3] — 2026-08-25

### Fixed

- **Wild Fox tame path no longer duplicates a wild fox.** The 1.15.0 "Wild Fox tame path" shipped an
  entirely separate hand-authored species (`Animals/Fox.json`, `NPCAgent/Agent_WildFox.json`,
  `Encounter/Encounter_WildFox.json`, 4 `NPCStat/Stat_WildFox_*.json` shells, `EncounterGuards/
  FoxGuard.json`, `Patcher/WildFoxLifecyclePatch.cs`) that roamed alongside the game's own real wild
  foxes instead of extending them — two foxes doing the same job. Removed all of that and replaced it
  with a `GameSourceModify/` patch (`VanillaFox_Agent1.json`, `VanillaFox_Agent2.json`) that appends
  the "Attempt to Tame" drag-and-drop action directly onto the real vanilla `Agent_Fox1`/`Agent_Fox2`
  NPCAgents (the ones already spawned from vanilla's `BurrowFox1`/`BurrowFox2`). Same bait items
  (Springberries/Bilberries/Juniper Berries), same `fc_fox_companion` result. `Patcher/
  CompanionHuntPatch.cs`'s `FoxTameInit` handler now matches the tame action against those two real
  agent UIDs and retires the tamed instance via the vanilla `AgentExists` NPCStat instead of a
  custom one. **Not yet verified in-game** — same unverified status the original 1.15.0 feature had.

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
