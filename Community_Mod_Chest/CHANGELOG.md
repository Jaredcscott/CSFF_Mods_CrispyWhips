# Community Mod Chest — Changelog

All notable changes to this mod are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.68.1] — 2026-08-24

### Changed

- **NPC scheduler performance: cached resolved NPC references instead of re-scanning the full NPC roster every tick.** Player-reported performance concern: the game's own NPC pathfinding/AI is known to be costly (community-reported since a past game update), and CMC's growing roster of scheduled NPCs (Apothecary, Professor, Miller, Weaver, the 4 Village Guards, InnKeeper, Ash, and the 3 Cottage residents) compounds it. Investigation found a distinct, independently-fixable inefficiency layered on top: about a dozen 1–3 second pollers (`ApothecarySchedulePatch`, `ProfessorSchedulePatch`, `CottageResidentSchedulePatch`, `CottageResidentSpawnPatch`, `GuardSpawnPatch`, `InnKeeperSpawnPatch`, `AshPartnerSpawnPatch`) each ran their own full linear scan of `GameManager.AllNPCs` every tick, forever, purely to re-confirm an NPC that had already been found and hadn't gone anywhere — `GuardSpawnPatch` alone did this 4× per second for the life of every save. Each now caches its resolved `InGameNPC` reference (mirroring the pattern `CSFFModFramework/Animals/AnimalLifecycleTicker` already used correctly) and only re-scans the roster when the cached reference is actually gone (destroyed/despawned) or hasn't been found yet — a new `Reflect.IsAlive` framework helper (CSFFModFramework 2.25.7) handles the Unity-destroyed-object check safely. Behavior is unchanged; this only removes redundant work in the common steady-state case.
- **`CompanionFollowDiagnostics` (a still-armed temporary diagnostic tracking whether companions can cross into modded map nodes, pending confirmation of a framework-level pathfinding fix) no longer re-runs its expensive real A\* pathfind probe on every player environment change while a companion is stuck** — its dedup key tracked the specific player environment, so while reproducing the exact bug it exists to catch, it re-probed on almost every 15-second tick as the player kept moving. It now dedups on stuck-vs-caught-up state alone, still logging every real transition, at a fraction of the cost.

## [1.68.0] — 2026-08-24

### Added

- **New "Quiet Village" character-creation trait (performance/accessibility).** Trades away the village's roaming NPC content for reduced simulation overhead: the Miller, Weaver, Apothecary, and Professor stay at home, the Academy, or the Inn instead of commuting or foraging, and none of the four Town Watch guards ever take up their posts — their patrol/chase/warden/summon duties are never spawned or evaluated for the whole run. The Inn Keeper needed no change, since he never leaves the Inn under any schedule. Free (0 Suns) — this is an opt-in accessibility toggle, not a gameplay advantage.

## [1.67.7] — 2026-08-23

### Fixed

- **Bleeder inflicted a real, unstoppable bleeding status with no wounds present, instead of just making existing wounds bleed longer.** The trait applied an unbounded per-tick RateModifier to the player's BloodLoss stat, so BloodLoss climbed toward its max and stayed there indefinitely — there was no way to actually recover from it in-game short of a witch ritual. Bleeder (and the same runaway-rate pattern on `Aged`, `DeadlyDisease`, `Fugitive`, `Leper`, `LostTourist`, `SeasonalAllergies`, `SensitiveSkin`, and `SpirituallyTroubled`) now apply a fixed, permanent stat offset instead — matching how the base game's own harsh drawback traits (Weak Immune System, Pain Sensitivity) are built: a constant `ValueModifier` with `RateModifier` zeroed, not an ever-increasing rate. These traits still make Pain/Nausea/Rash/Stress/Fear/Loneliness/SunAllergy/BloodLoss permanently worse, but at a fixed, survivable severity instead of spiraling to a maxed-out stat over a few in-game days.
- **Depositing higher-value Duros Coins (Ghost Copper, Bronze, Iron, White Copper, Pure Tin) at the Inn/Academy counter credited far less than the coin's actual worth**, even though trading the same coin with the innkeeper paid full value. The deposit formula approximated a coin's value from its raw metal-purity stat (`SpecialDurability4`), which only happens to line up with a plain Copper Coin — every higher denomination is priced by DurosCoinage as its own worth, not metal purity, so the approximation undervalued them (e.g. a 1800-value Pure Tin Coin credited only 60). Deposits now read the coin's own `TradingValue` — the same field the innkeeper correctly uses — falling back to the old metal-purity formula only if a coin has no trading value set.

### Added

- **Two new milder Courage traits: Weak Courage and Weak Cowardice.** The base game's Brave/Fainthearted traits swing Courage by a full ±10000 — enough to make a character permanently fearless or permanently unable to push through scary actions regardless of circumstance. These new traits shift Courage by a much smaller fixed +50/-100, leaving room for other factors (like Pain) to still tip the balance — e.g. enough courage to grit through stitching a wound at high Pain, but not enough to do it comfortably at low Pain.

## [1.67.6] — 2026-08-22

### Changed

- **Foot Wraps got new card art depicting an actual reed/rush weave** — the item's description and blueprint always called for dried reed and plant fiber, but its art was a duplicate of the Hand Wraps illustration (a woven cloth strip), so the two looked identical despite being different materials.
- **Hand Wraps no longer require Reeds to craft.** Its art has always depicted plain cloth strips (and the Chinese localization already described it that way), so the item and blueprint now match: it's built from a scrap of cloth (`ClothSmall`) instead of Dry Reeds, dropped `tag_ReedClothing`, and its "Rip up" action now returns cloth instead of reeds. Still freely craftable from the start, same as before.

## [1.67.5] — 2026-08-22

### Fixed

- **Duplicate trees on new map-expansion tiles (e.g. Pine Trail showing two Small Pine Tree, two Pine Tree, and a Birch Tree x2 stack).** Nine of the twelve CMC map nodes (`cmcEnvPineTrail`, `cmcEnvHighGrove`, `cmcEnvClayFlats`, `cmcEnvMarshHollow`, `cmcEnvMossyClearing`, `cmcEnvForagingForest`, `cmcEnvHuntersCrossing`, `cmcEnvDeerMeadow`, `cmcEnvBadgerWarren`) clone a vanilla location without stripping its native "Create Small/Large/Birch Tree" regrowth action, which could race `TreeRespawnPatch`'s own daily/env-entry tree check and plant a second copy of the same species. `TreeRespawnPatch` now trims any species that exceeds its declared per-location count in addition to filling shortfalls, so both future races and already-duplicated boards self-correct on the next day rollover or env visit. (The tree species were deliberately kept out of `StripLegacyBoardUIDs` — that list also triggers a full env-board wipe-and-reseed if a listed UID is ever found present on a saved board, which would fire on every load for a species this patch intentionally keeps on the board forever.)

## [1.67.4] — 2026-08-22

### Changed

- **Partner Inn/Academy following: diagnostics promoted from invisible to logged.** `PartnerIndoorFollowPatch` (shipped 1.47.1) has never been confirmed in-game crossing either doorway. Its only "it worked" line was `LogDebug`, which BepInEx suppresses by default, so a successful run and a silent no-op looked identical in the log. Boundary crossings, per-companion relocation outcomes, and failure paths (missing refs, missing `GameManager`) now log at `LogInfo` so the next playthrough that walks a companion through the Inn or Academy door will show conclusively whether it worked.

## [1.67.3] — 2026-08-22

### Changed

- **Town Achievement Board benched pending further work.** A fresh-save playtest found the board's in-game presentation broken — several of the eleven achievement entries and the "N of 11 earned" progress summary don't render. Rather than ship a half-working board, the whole subsystem is disabled: `cmcBoardAchievements` is removed from the Village Inn's default spawns, and the three supporting patches (`AchievementBoardSeedPatch`, `AchievementTrackerPatch`, `AchievementKillEffectsPatch`) are commented out in `Plugin.cs`. No code was deleted — the board's CardData, all `cmcStatAch*` GameStats, and the patch classes remain in the repo for the next work session. Player-facing mentions removed from `README.md` and `ModInfo.json` until it's ready.

## [1.67.2] — 2026-08-22

### Fixed

- **Digging a winter Snow Drift "by hand" (no shovel) permanently softlocked that road.** Each Snow Drift (Village↔Pine Trail, Village↔Deer Meadow/Stillwater Meadow, Village↔Village Farm) offers two ways to clear it: a shovel `CardInteraction`, or a slower no-tool `DismantleAction`. The framework's gate-reopen listener was only wired to the shovel action's key prefix, so clearing the drift by hand destroyed the card exactly as advertised but never told the road gate to reopen — the connection stayed locked for the rest of the game, even after a full clear. Renamed the by-hand action's internal key to share the shovel action's prefix so either method now correctly reopens the road. A road already stuck locked from digging by hand before this fix needs the Snow Drift to regrow and be cleared again (or a manual save edit) — this only prevents new occurrences.

## [1.67.1] — 2026-08-22

### Fixed

- **Town Achievement Board: "Finding a Friend" could show as already earned on Day 1-2 of a brand-new save.** The detector checked only whether a Trader NPC existed anywhere in the game's NPC roster (`GameManager.AllNPCs`) — but Traders are instantiated into that roster during normal game boot, regardless of whether the player has ever actually been near one. It now additionally requires the trader to be on the player's current board at the moment of the check, matching the achievement's "cross paths with a traveling trader" description. Note: a save where this already latched incorrectly will keep showing it as earned — the fix only prevents new false positives.

## [1.67.0] — 2026-08-21

### Added

- **The Miller and the Weaver now genuinely go to work.** Previously, once their weekly Academy day and nightly Inn visit were accounted for, their remaining daytime hours were a purely cosmetic roll to wander generic outdoor nodes (Village Path/Farm, Foraging Forest, Pine Trail, Highland Pines, Moss-Grown Clearing) — no profession-specific action ever fired there, which is what player reports of "they don't enter the mill/workshop" were really describing. They now hold a real "at work" duty (built on the same engine `NPCDuty` chassis the Village Guards' patrol already uses) that keeps them at the Village during the day, and while there they top up their own trade stock once a day — the Miller grinding a little Wheat/Rye/Acorn Flour, the Weaver working the loom for Spindle, Bone Needle, Twine, and Yarn Fiber. Distinct from their existing weekly Copper Chest savings — this is a separate, smaller daily top-up to their own carried satchel.

### Fixed

- **README's Cottage Residents section still described the pre-1.66.0 "won't head to the Inn/Academy until you've visited that interior yourself at least once" limitation**, which no longer applies — corrected alongside the wording above.

## [1.66.0] — 2026-08-21

### Added

- **Winter snow drifts blocking the Village's three roads can now be dug through by hand, not just with a shovel.** Each `cmcSnowDrift{North,South,East}` gained a "Dig Through the Snow Drift by Hand" self-action — no tool required, same 3-hit clear, but 20 DTP per hit instead of 8 (2.5x slower), so a character caught out in winter with no shovel isn't hard-blocked from ever reaching the Village.
- **Foot Wraps and Hand Wraps now craft from Fiber + Reeds + Twine instead of Small Cloth**, joining the reed-based equipment line (`tag_ReedClothing`) alongside the vanilla Reed Coat/Tunic/Skirt. Previously both recipes required Small Cloth, which meant they were only obtainable once the Loom (a mid-game Weaving-skill unlock) was already researched — by which point most players already have leather boots/gloves and no longer need improvised wraps at all. The reed-based recipe keeps them genuinely early-game, matching their "crude improvised" flavor text and their placement in the lowest Tailoring subtab. Flavor text and the research-gate item both updated to match (now gated on having Reeds in hand, not Cloth).

### Fixed

- **Traveling toward the Village from the south (via Pine Trail) could skip straight into the Village instead of stopping there.** Root cause: 11 of the 12 CMC WorldMap clone nodes (not just the Village, previously fixed in 1.65.2) inherited the same 8 stray vanilla road/fence improvements (`Imp_PathNorth/East/South/West`, `Imp_HuntingFencesNorth/East/South/West`) from their own clone templates — unlocked, freely buildable, with a travel-destination cache that can go stale against this mod's own seasonal snow-drift gates. `VillageStrayImprovementsPatch` is now `StrayImprovementsPatch`, generalized to strip all 8 stray GUIDs from every CMC location, plus a new boot-time pass that also un-marks any of them already built on an existing save.
- **The Professor's Commissions option (and the "3 Nettle Leaves" specimen request specifically) could disappear and reappear while entering/leaving buildings, even while standing right in front of him.** His phase-downgrade check (Resident → Foraging, which hides all Commissions) was missing the same `withPlayer` guard his movement logic already had, so his satchel dipping to ≤2 while the player was mid-conversation would instantly hide every commission.
- **Giving the Professor a finished Cloth Coat for his Weaver interlock errand consumed the coat but never advanced the quest, so he asked for another one immediately.** The "Give Cloth Coat" drag-and-drop action on his agent was missing the `StatModifications` bump every sibling "Give X" action has; added, matching the existing pattern.
- **The Miller and Weaver never actually entered their own cottage interiors overnight.** Their schedule required the player to have personally opened the Miller's/Weaver's cottage door at least once that session before it could path them there — something almost no player ever does — so they stood outside in the Village indefinitely instead. Both interiors are non-instanced, so the "must be visited first" requirement was unnecessary; their destination `EnvID`s are now built the same way every outdoor wander node's already was, with no prior visit needed.

### Investigated, not a bug

- **New map areas having several large trees plus small branches.** `TreeRespawnPatch`'s per-node tree targets are hard-capped (never spawns beyond the declared count) and confirmed against each node's real vanilla clone template — most nodes match 1:1, a handful deliberately set the secondary/decorative tree species one or two higher for variety. No runaway growth is possible; this is intentional forest density, not a bug.

---

## [1.65.5] — 2026-08-20

### Changed

- **Village Inn/Academy/Village Hall warmth no longer comes from a forced stat hack — it comes from a real fire.** 1.65.2 patched the "Hearth-Warmed" PassiveEffect's runaway `RateModifier` (which cooked a player to death in the Inn) down to a small `+2/tick`, backed by a compensating `IndoorHeatCapPatch` that force-set Body Temperature to a target value every tick. That's two hacks fighting over one stat, unconditionally active whether or not the room was actually "warm" in any in-fiction sense. Replaced instead: the "Hearth-Warmed" PassiveEffect and `IndoorHeatCapPatch` are both removed outright, and the Academy and Village Hall now each get their own vanilla Fireplace dropped into their interior on first visit (`DefaultEnvCardDrops`), same as the Inn already had. Warmth now comes entirely from vanilla's own Fireplace mechanism — its "Body Temp"/"Indoors Temp" PassiveEffects, present only on the lit CardData and gone the instant it goes out — the exact same source every player-built fireplace already uses, so it naturally settles at a comfortable level and can't cook anyone. `InnFireplacePatch` (which kept the Inn's hearth topped off and auto-relit) is generalized into `VillageFireplacePatch`, which now maintains all three buildings' own built-in hearths the same way.

## [1.65.4] — 2026-08-20

### Changed

- **Wicker Chair blueprint now actually calls for wicker.** The recipe previously asked for 3 Wooden Plank + 3 Rope, which doesn't read as "wicker" at all. It's now 2 Wooden Plank (frame) + 4 Dry Reeds (weaving) + 2 Twine (binding), plus a Cutting Tool held (not consumed, GpTag_CuttingTool group) to trim the reeds during construction — the tool takes 8 Usage wear per build, same pattern as this mod's other tool-gated blueprints. Take Apart and the card's help text now return/describe Plank, Reeds, and Twine instead of the old Plank/Rope pair.

## [1.65.3] — 2026-08-20

### Changed

- **The Outfit Wardrobe's storage now labels each outfit's slots directly on the grid.** The 54-slot inventory previously rendered as one flat, paginated grid with no visual boundary between the three 18-slot outfit blocks, and the page-arrow scroll width doesn't land on those boundaries either — there was no way to tell where one outfit's slots ended and the next began, so a player had no reliable way to choose which outfit an item was going into. "Outfit 1/2/3" headers now sit directly on the first slot of each block (`OutfitWardrobeSectionsPatch.cs`), and clicking any "Equip/Unequip Outfit N" button now also scrolls the popup straight to that block, whether or not the click itself equips/unequips anything.

## [1.65.2] — 2026-08-20

### Fixed

- **Village/Academy/Village Hall interiors could cook a player to death from heat.** The "Hearth-Warmed" PassiveEffect on all three interiors applied an unconditional `RateModifier: +15/tick` to Body Temperature — stronger than even vanilla's own emergency Hyperthermia cool-down rate (-12/tick, applied only once you're already dying of heat stroke) and wildly out of proportion to the "small Comfort bonus" this mod's own description promises. The existing runtime safety net (`IndoorHeatCapPatch`) only covered the Inn and Academy — Village Hall had no correction at all — and even where present, its 0.75 target fraction landed at Body Temperature 125 (the "Sweating" band), not the "Comfortable" band (86-114), which is why a player still died of heat stroke in the Inn despite it. Fixed at the root: the RateModifier is now `+2/tick` in all three interior JSONs. `IndoorHeatCapPatch` now also covers Village Hall and targets a genuinely safe 0.65 fraction (value 95, mid-Comfortable) as defense-in-depth. **Superseded the same day — see 1.65.5.**
- **Visiting the Inn could turn a player's own fireplace into a "full fuel, no heat" fireplace.** `InnFireplacePatch`'s auto-relight logic matched *any* card on the Inn's board sharing the vanilla Fireplace UID — since `UniqueOnBoard` is false on vanilla Fireplace, a player who built their own fireplace inside the Inn had it caught by the same logic and force-refueled via reflection, which doesn't go through the normal ignition path (so it showed full fuel but produced no heat until manually extinguished and relit). The patch now backs off entirely unless exactly one fireplace-type card is present on the board, so it can no longer act on a player-owned one.
- **Building "a road" at the Village could permanently block travel south.** `cmcLocVillage` is cloned from vanilla `ClearingOak_GreenGlade`, whose CT8 template carries 8 vanilla improvements (`Imp_PathNorth/East/South/West`, `Imp_HuntingFencesNorth/East/South/West`) that CMC never intended to inherit — unlocked, freely buildable, with no CMC content behind them. Completing one of the Path improvements resolves its destination from a cache built once when the Village's CT8 first loads and never rebuilt after the Village's own seasonal snow-drift gates strip/restore travel actions — letting the resolved destination silently diverge from the live travel state (reported: building the road and then traveling south looped back to the Village instead). `VillageStrayImprovementsPatch` now strips all 8 stray GUIDs from the Village's `EnvironmentImprovements` every boot, before a player can ever see or build them.

### Investigated, not yet actionable

- **Two overlapping locations shown in the world map's top-left corner.** No coordinate collision found among any of this repo's mods (CMC/ACT/H&F) or against vanilla `DefaultWorldMap.json`, and the framework's `CoordRegistry` would loudly reject a same-session collision rather than silently double-render one — so the cause isn't visible from source alone. Needs the player's installed mod list, a map screenshot, and the `[CoordRegistry]`/`WorldMapInjector:` lines from their BepInEx log to diagnose further.
- **A path to a waterfall north of the Village that doesn't seem to go anywhere.** This is very likely the intentional, gated Sett Warren → Greenfalls shortcut (`cmcEnvBadgerWarren`'s `VanillaExits`), which only opens after building a Climbing Rope at Greenfalls itself via the normal vanilla route — not a bug. `ConnectionGateService` is documented to fully hide a locked connection's line on the map, so if the player is seeing a visible-but-non-functional route before ever building the rope, that would point to a gate-evaluation bug rather than working-as-intended content; unconfirmed without a log from a reproduction.

## [1.65.1] — 2026-08-19

### Fixed

- **Outfit Wardrobe blueprint's Carpentry-course unlock gate was broken.** `AcademyCourseService.cs`'s `Carpentry` course-unlock table still referenced the three blueprint UIDs (`cmcbpclothesrack`, `cmcbpcoatrack`, `cmcbpwardrobe`) that 1.65.0 deleted when it consolidated them into the Outfit Wardrobe, and was never updated to reference the new blueprint (`cmcbpoutfitwardrobe`) in their place. The dead UIDs silently no-opped (treated identically to "owning mod not installed"), and the real blueprint was never touched by the course-gating pass at all — leaving it either permanently locked or freely researchable without graduating, contradicting both the blueprint's own `UnlockConditionsDesc` and the graduate perk's description. Fixed by swapping the table entry to `cmcbpoutfitwardrobe` (found by `/audit-mod`, 2026-08-19).

## [1.65.0] — 2026-08-17

Merged the Carpentry course's Clothes Rack, Coat Rack, and Wardrobe, plus the separately-shipped Weapon Rack & Armor Stand, into one furniture piece: the **Outfit Wardrobe**. Also fixes two confirmed latent bugs in the old Dress/Undress mechanic those items shared.

### Changed

- **Outfit Wardrobe (`cmcOutfitWardrobe`, blueprint `Bp_OutfitWardrobe.json`)** replaces Clothes Rack (`cmcClothesRack`), Coat Rack (`cmcCoatRack`), Wardrobe (`cmcWardrobe`), and Weapon Rack & Armor Stand (`cmcWeaponRack`) — all four are removed. One placed item, 54 `InventorySlots` split into three fixed 18-slot sections (one per non-wound `EquipmentTag`, matching the old Clothes Rack's slot count), each with its own **Equip Outfit N** / **Unequip Outfit N** DismantleAction pair — three complete, independently swappable gear sets instead of one flat Dress/Undress rack. Keeps the Weapon Rack's ambient Comfort (+6) passive effect. Gated the same as the old Clothes Rack/Wardrobe (Carpentry course graduation + at Foraging Forest) rather than the Weapon Rack's no-requirement tier. `BlueprintTabs.json`'s Furniture tab, `CMC_CarpentryBench.json`'s final-exam description, and `Pk_GradCarpentry.json`'s perk description all updated to match.
- Localization: removed all 36 EN/CN rows for the four retired items, added 19 rows for the Outfit Wardrobe (`SimpEn.csv` + `SimpCn.csv`, in the same commit per root CLAUDE.md's localization-parity rule).

### Fixed

- **`ClothesRackEquipService`'s "Dress" (rack → body) direction never actually equipped anything**, on both the old Clothes Rack and Wardrobe, confirmed by static analysis of the decompiled engine types (not yet reproduced in-game): (1) it read the rack's contents via `CardUtil.GetInventoryList`, which resolves `InGameCardBase.CardsInInventory` — a `List<InventorySlot>` wrapper list, not the actual cards — so every entry it iterated was an `InventorySlot`, not a card; (2) its `IsAlive()` Unity-object null check assumed every reflected object was a `UnityEngine.Object`, but both `InventorySlot` and `DynamicLayoutSlot` are plain C# classes, so that check was always false for them regardless of the first bug. Together, "Dress" always iterated zero items and silently logged "nothing in the rack could be equipped" no matter what was actually stored. "Undress" (body → rack) was unaffected by either bug.
- New **`OutfitWardrobeEquipService`** rewrites the whole mechanism using direct compile-time types against the mod's already-referenced `Assembly-CSharp-nstrip.dll` instead of reflection (removing both bugs' root cause), and adds a third fix needed for per-outfit sections specifically: `InGameCardBase.DropInInventory`'s target-slot search ignores its `_From` parameter for a normal (non-legacy) inventory and always scans from index 0, so it cannot be scoped to one outfit's 18-slot block — a naive "Unequip Outfit 2" could land an item in Outfit 1 or 3's section instead. `TryPlaceInOutfit` finds a free slot within the target section itself and replicates `DropInInventory`'s placement steps with that manually-chosen index.

## [1.64.0] — 2026-08-16

Inn Achievement Board (`Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md` §10.9), Wave 3 — the final six detectors. Prompt 7 of the pack (the last one); Prompt 6 (1.63.0) shipped the two kill-driven detectors. **All eleven achievements now have a wired detector — code-complete and build-verified. An in-game playthrough confirming every one fires correctly (the plan's §10.9.5 acid test) has NOT been run yet; this release does not claim in-game verification.**

### Added

- **`AchievementTrackerPatch`** gains six more branches on its existing 5-second poll:
  - **Full Kit / Master Angler / Master Shaman** now share ONE `GameManager.AllCards` scan (`CheckCollectionsAndStinkyJar`), reusing the same object-gm → IEnumerable AllCards → CardModel → UniqueID walk `LostCatPatch.CatExistsAnywhere` established rather than opening three separate loops. Each card's UID is checked against whichever of the three curated item-UID lists (6 metal tools / 6 fish / 19 bound spirits) isn't already fully earned; a sighting latches "possessed at least once" and the derived count/earned stats recompute only when something new latched.
  - **Stinky Jar** rides the SAME scan: a storage-pot card (4 curated UIDs — unsealed/sealed, item and placed forms) whose `ContainedLiquidModel` is one of the 3 Urine aging stages and whose `ContainedLiquid.CurrentLiquidQuantity` has reached the pot's own `CurrentMaxLiquidQuantity`. Single boolean earned latch, no member/count stats.
  - **Spiritual Overcrowding** is its own small poll branch (a per-player stat read, not a card scan): earns when the vanilla Spiritual Noise GameStat's current value reaches its own LIVE maximum, read via a new `HiddenStat.GetMax` (`CurrentMinMaxValue.y`) rather than the JSON default of 13, since perks can shift the ceiling at runtime.
  - **Finding a Friend** is also its own poll branch: `GameManager.FindNPC` against the 3 Trader NPCAgent SOs, checked with the same compile-time `UniqueIDScriptable.GetFromID<T>` idiom `AchievementKillEffectsPatch` established — safe because a statically-referenced `GameManager`/`NPCAgent` symbol binds at compile time to the one Assembly-CSharp.dll the csproj references, unlike the broad runtime type-name scans the ModCore-shadowing warning in root CLAUDE.md is actually about.
- **`HiddenStat.GetMax(string statUid)`** — new accessor alongside the existing `Get`/`Set`, wrapping `StatAccess.GetMaxValue` on the same resolved GameStat instance. Added rather than duplicating the GetFromID+StatsDict resolution chain a third time.

### Fixed

- **Overclaiming "craft"/"catch" wording** — Master Angler's earned line ("...has been hooked and hauled to shore") and Master Shaman's locked line ("Bind the spirit of every...") both implied the player personally performed the acquisition action (fishing / a binding ritual), which an AllCards sighting can never actually confirm — a possessed-via-trade item counts identically. Reworded both (JSON `DefaultText` + `SimpEn.csv` + `SimpCn.csv`, all three realigned in this commit) to a possession framing ("has passed through your hands at least once" / "Come to possess a bound spirit of every kind... you've ever held"). Full Kit's existing wording ("Assemble the complete set...") was checked and left as-is — it doesn't specify a craft-only acquisition method.
- **`VillageHallBoardsPatch`'s stale doc comment** — it previously said Full Kit/Hunter/Angler/Shaman "read as 0 until their Wave 2 detectors ship (a later prompt)"; updated to reflect that all five multi-part achievements now have a live detector.

### Notes

- README.md / ModInfo.json Description / this changelog all now describe the board as eleven-of-eleven code-complete rather than five-of-eleven — docs-honesty pass in the same commit as the wiring, per root CLAUDE.md § Docs-Honesty on Behavior Change. None of the three claims in-game verification.

## [1.63.0] — 2026-08-16

Inn Achievement Board (`Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md` §10.9), Wave 2 — the two kill-driven detectors. Prompt 6 of the pack; Prompt 5 (1.62.0) shipped board rendering plus the first three detectors.

### Added

- **Right to Bear Arms** and **Master Hunter** now actually track. Felling a Bear earns Right to Bear Arms outright; hunting each of the twelve huntable animals (Badger, Bear, Beaver, Boar, Doe, Duck, Fox, Hare, Partridge, Squirrel, Stag, Wolf) latches that animal, and the board's "Master Hunter: N of 12" line advances as you go. Repeat kills of the same animal never double-count. That brings the board to five of eleven working detectors; the remaining six (Finding a Friend, Full Kit, Master Angler, Master Shaman, Spiritual Overcrowding, Stinky Jar) are still a follow-up release.
- **`AchievementKillEffectsPatch.cs`** — appends a clamped `+1` `StatModifier` onto the `EnemyDefeatedEffects.StatChanges` of each of the twelve animals' **vanilla** `Encounter` assets (Bear gets two: its Master Hunter member latch plus the standalone `cmcStatAchBearSlain`). Runs on `FrameworkEvents.GameDataReady` — the tail of the framework's own `GameLoad.LoadMainGameData` postfix, so WarpResolver has already run and the resolved `GameStat` reference is set directly rather than via a (by then dead) `StatWarpData` string. The append copies the existing array and grows it by one, deliberately: all twelve encounters already carry 2–5 vanilla entries (Gratification, BloodSpilled, ViolenceTracker, FoxUrge, Pop_Squirrel, BadgerDefeated), and assigning a fresh array would have silently deleted every animal's vanilla kill rewards. Idempotent — an entry already targeting that stat is skipped, so repeated `LoadMainGameData` calls in one process cannot stack duplicates.
- **`AchievementTrackerPatch`** gains a Master Hunter branch on its existing 5-second poll: derives `cmcStatAchHunterCount` and the earned latch from the twelve member latches the kill effects set, writing only on an actual change and short-circuiting entirely once the achievement is earned.

### Notes

- A `GameSourceModify/` JSON patch was evaluated for this and ruled out on two independent grounds, both confirmed against framework source rather than assumed: `StatChanges` lives on the nested `EncounterResultEffect` object, which `GameSourceModifier.ApplyAppendArrays`' flat `GetField` lookup cannot reach; and it is a fixed-size `StatModifier[]`, which that method's `list.Add()` append cannot grow. Vanilla `Encounter` objects themselves *are* GSM-targetable (unlike the scene-scoped `StatListTab` assets that defeated `StatTabInjectionPatch`) — the blocker is the nested fixed-size array, not the target's availability.

## [1.62.0] — 2026-08-16

Inn Achievement Board (`Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md` §10.9), Wave 1 completion — board prose-rendering plus the first three (of eleven) working detectors. Prompt 5 of the pack; Prompt 4 (1.61.0) shipped the board/stats chassis with zero live detection.

### Added

- **Achievement Board prose rendering** — `cmcBoardAchievements` joins `VillageHallBoardsPatch`'s existing board-description postfix (the same "read-only, no button strip" pattern as the six villager/Town boards). Its description now appends an `"Achievements earned: N of 11"` summary line (counting the eleven earned-latch stats ≥ 0.5) plus a `"<Achievement>: N of <total>"` progress line for each of the five multi-part achievements (Full Kit, Master Hunter, Master Angler, Master Shaman, Forest Explorer, Spelunker), read from their derived count stats.
- **`AchievementTrackerPatch.cs`** — a new 5-second poll (the only new `TickEvents.Interval` this pass) implementing three of the eleven detectors:
  - **Happy New Year** — earns when `GameManager.CurrentDay` reaches the gamemode's actual year length (`GameManager.DaySettings.DaysPerYear`, read live via a new `GameQuery.DaysPerYear` accessor — never hardcoded).
  - **Forest Explorer** / **Spelunker** — intersect `GameManager.VisitedEnvironments` against two Wave-0-curated vanilla environment-UID lists (10 surface groves/thickets/clearings, 8 caves); each newly-visited member latches its own hidden stat, the derived count stat recomputes, and the achievement's earned latch flips once every member is latched. Each detector short-circuits on its own earned latch before touching `VisitedEnvironments`, so a settled save costs a handful of stat reads per poll.
  - The remaining eight detectors (Finding a Friend, Full Kit, Right to Bear Arms, Master Hunter, Master Angler, Master Shaman, Spiritual Overcrowding, Stinky Jar) are Wave 2 — a later prompt in this pack.
- **`GameQuery.DaysPerYear`** (`CSFFModFramework/Api/GameQuery.cs`) — new read-only accessor for `GameManager.DaySettings.DaysPerYear`, following the same lazy-reflection-cache pattern as the existing `DaysPerMoon`/`CurrentDay`. Falls back to 120 (the confirmed EA 0.66h default) before game init or on a read failure, so a `CurrentDay >= DaysPerYear` comparison can never false-trigger at boot.

## [1.61.0] — 2026-08-16

Inn Achievement Board (`Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md` §10.9), Wave 1 chassis — the first of a multi-part implementation pack. This release ships the board, its full 22-line entry set, and the hidden-stat scaffolding; it does **not** ship detection, so every entry currently reads as not yet earned. Detection and the board's prose-rendering are follow-up releases.

### Added

- **Town Achievement Board** (`cmcBoardAchievements`) — a new notice board in the Village Inn, copied from `CMC_BoardTown.json`'s chassis. Lists eleven achievements, two `DismantleActions` lines each (locked/earned, gated by `RequiredStatValues` half-open bands on that achievement's own earned-latch stat): Finding a Friend, Full Kit, Right to Bear Arms, Master Hunter, Forest Explorer, Happy New Year, Master Angler, Spelunker, Master Shaman, Spiritual Overcrowding, and Stinky Jar. Spiritual Overcrowding and Stinky Jar keep cryptic locked-line flavor ("The innkeeper refuses to explain this entry.") in the spirit of the plan's joke achievements.
- **79 hidden `GameStat/CMC_Ach*.json` files** — 11 earned latches (one per achievement), 61 member latches (one per item in the five multi-part achievements: 6 metal tools, 12 huntable animals, 6 fish, 19 bound spirits, 10 surface environments, 8 caves), 6 derived count stats (`cmcStatAchFullKitCount`, `cmcStatAchHunterCount`, `cmcStatAchAnglerCount`, `cmcStatAchShamanCount`, `cmcStatAchExplorerCount`, `cmcStatAchSpelunkerCount`, each `MinMaxValue` bounded to its own list size), and `cmcStatAchBoardPlaced` (the seeding latch below). All generated by a throwaway script (`Development_Tools/Generate-AchievementBoardContent.py`) rather than hand-authored, per the plan's own instruction. Member-latch stat UIDs follow `cmcStatAch<Category><Item>` (e.g. `cmcStatAchToolAxe`, `cmcStatAchHuntBoar`, `cmcStatAchFishTrout`, `cmcStatAchShamanBadger`, `cmcStatAchEnvSacredGrove`, `cmcStatAchCaveBearCave`), matching each JSON file's own `CMC_Ach<Category><Item>.json` name.
- **Seeding**: `CMC_InnInterior.json`'s `DefaultEnvCardDrops` now includes the board for fresh saves. A new `AchievementBoardSeedPatch.cs` backfills it for existing saves that had already visited the Inn — same deferred-spawn shape as `CopperChestPatch`/`LostCatPatch` (gate on the player's current environment, duplicate-scan before spawning, latch `cmcStatAchBoardPlaced` so it never respawns).

## [1.60.1] — 2026-08-16

Fixed: Stone Tile Floor (`cmcimpstonetilefloor`) was missing from the Cabin's Attic room (`07af3f7fc01dd6e48920b19ed63c0d48`) in `InjectImprovementInto.json` — every other livable cabin/mud-hut room (Cabin main room, under-construction Cabin, Cabin Room, Mud Hut main room, under-construction Mud Hut, and both Mud Hut expansion rooms) already had it, but the Attic was the one room in the "can be laid in cabins and mud huts" description that couldn't build it. Added the missing target entry (now 9 total).

## [1.60.0] — 2026-08-16

Batch implementation of the mod's open audit-derived Near-Term backlog (`Documentation/Plans/Community_Mod_Chest/Audit_Remediation_Plan.md`, N1–N22) — 19 of 22 ideas shipped this pass; three fleet-tooling items and one blocked-by-design item are called out separately below.

### Added

- **Clay Ocarina.** A craftable pottery-tier instrument that now also counts toward taming Shadow the Cat, closing a real gap where a player who never crafted a vanilla flute/drum had no path to the Apothecary's Cabin quest chain.
- **Medicine course payoff — Herb Poultice + Tincture.** The Academy's Medicine graduates now unlock two treatment items (an active bleed/pain poultice, a nausea/pain/rash/stress tincture) gated the same way every other course's payoff is, closing the one course that previously granted no craftable reward.
- **Old Growth Bark now tans leather.** A new "Tan Hide" interaction on the bark follows through on its own description's tannin claim, turning a fleshed hide into usable leather.
- **Toys & Games line.** Bone Dice, Spinning Top, and Wheeled Horse — three cheap, always-buildable comfort flavor items in the Entertainment tab.
- **Dyed apparel variants.** Cloth Coat, Chaperon, and Cloth Scarf can each be dyed with Pigment into a cosmetic charcoal-toned variant (Charcoal-Grey, Soot-Black, Ash-Grey) with a modest trade-value bump; purely cosmetic, no new passive effects.
- **Scented Candle.** A beeswax-fueled comfort light source alongside the existing Incense Burner and Ceramic Lamp.
- **Themed starting traits — Potter's Apprentice and Caravan Peddler.** Two new character-creation packages bundling pottery-starter or trading-starter goods, in the same style as the Founders Kit trait.
- **Burglar's Kit.** A craftable tool that shaves a small amount off a Copper Chest "Search for valuables" detection roll when carried.
- **"Make It Right" restitution.** Dragging Salt or a Metal Nugget onto the Miller's Copper Chest now pays down a small amount of Village Crime — the first voluntary, non-punitive way to walk crime back down.
- **Selling into a Copper Chest now builds the resident's Trust.** All five residents gain a small Trust bump per completed sale; the Apothecary gained a full new Trust stat (mirroring Miller/Weaver/Professor) for parity, while the Inn Keeper's sale bump writes his existing friendship stat instead of duplicating it.
- **A civic tell for Village Crime standing.** The Town board now shows a four-tier, softly-worded read of the player's standing with the village — a way to check your reputation without waiting to be confronted by a guard.
- **Cat Bed.** A new placed comfort item, plus a "Give a Treat" interaction on all three cats (Ash, Shadow, and the tamed Stray) that raises their Care stat using the Iron Fishing Rod's own catch. Tamed cats now also show three Care-tier flavor lines in their description as Care rises or falls, so the stat is legible before it hits zero.
- **A reason to linger in resident interiors.** All five resident interiors (Miller, Weaver, Apothecary, Academy, Inn) now carry a small themed Comfort bonus while the player is inside.
- **Market Stall "Set Up / Take Down Awning."** A free cosmetic toggle between the stall's plain frame and a dressed, market-day appearance, preserving inventory and sales progress across the swap.
- **An autumn deadfall on the Pine Trail ↔ Highland Pines route.** A second seasonal barrier alongside the existing winter snow drifts, clearable with an axe once autumn arrives.
- **Weapon Rack & Armor Stand.** A new storage/display furniture piece sized for the mod's crafted combat set (Stone Mace, Club, Fire-Hardened Spear, Wooden Shield, Bone Lamellar, Quilted Vest/Cap), with a small ambient Comfort bonus.
- **Talk dialog for Guards Thorne, Corrin, and Vane.** The three remaining Town Watch members now have a friendly, always-available Talk option with a distinct register per guard, alongside Captain Sterling's existing crime-band Talk.
- **Dress/Undress now works on the Wardrobe, not just the Clothes Rack.** The 1.59.0 one-motion equip mechanism now serves both pieces of furniture.

### Fixed

- `Community_Mod_Chest/Patcher/AcademyCourseService.cs`'s Medicine course had a declared graduate-perk constant with no unlock table entry — the two new Medicine payoff blueprints are wired into it in this same release so they aren't shipped inert.

### Notes

- **Sling / Sling Stones (N11) stays deferred.** `Documentation/Ideas/Community_Mod_Chest/DEFERRED_ITEMS.md` is explicit that ranged-combat wiring is an open EA 0.66 engine question; shipping the items without a real ranged mechanic would misrepresent what they do, so this one idea from the backlog was intentionally not implemented this pass.
- Several new blueprints, items, and dialog lines ship with placeholder art/sprite names pending a dedicated art pass — see the Audit Remediation Plan history and each feature's own commit for specifics.

## [1.59.3] — 2026-08-16

### Fixed

- **Carpentry furniture set — blank blueprint/card icons.** Clothes Rack, Coat Rack, Wicker
  Chair, and Wardrobe shipped in 1.59.0 pointing `CardImageWarpData` at reused vanilla sprite
  names (`Bookshelf`, `Shelf`, `Basket`, `Chest`), which rendered as a blank placeholder in the
  Construction › Furniture blueprint tab instead of borrowed art. Repointed all four blueprint
  and placed-structure cards to dedicated `CMC_*` sprite names with white placeholders in place
  of the broken references; final custom art is still pending (prompts drafted in
  `Documentation/Community_Mod_Chest_Image_Prompts.md`).

## [1.59.2] — 2026-08-16

### Fixed

- **Stone Tile Floor — missing coverage on the Cabin's earliest construction stage.**
  `InjectImprovementInto.json` wired the improvement into 7 of the 8 cabin/mud-hut boards it
  should reach — the Mud Hut's starting-point (pre-wall) construction stage was included, but the
  Cabin's equivalent starting-point stage (`CabinConstructionStartingPointCabin`) was not, even
  though vanilla's own Windows/Fireplace/Sauna Stove improvements are all buildable there. Added
  the missing `TargetEnvUID` entry so Stone Tile Floor now reaches the same stage for both house
  types.

## [1.59.1] — 2026-08-16

### Fixed

- **Fishing Net — "Cast Net" now actually requires a riverbank.** The water gate used the wrong
  JSON shape for `RequiredTagsOnBoard` (a flat tag-name array, valid only for `GeneralCondition`),
  so it silently resolved to nothing and the action was available on any tile. Rebuilt using the
  proven per-condition `TriggerTagWarpData` shape (see WDI's `MillRaceOutlet_Kit.json`), and the
  action now hides entirely when not near `tag_River`.
- **Fishing Net — "Cast Net" now consumes a use.** `ReceivingCardChanges.ModType` was left at `0`
  (None), which is a no-op for durability changes regardless of `UsageChange`; the net's "Casts"
  stat never ticked down. Set to `1` (DurabilityChanges) so each cast now spends 1 of 10 uses
  before the net wears out.

## [1.59.0] — 2026-08-16

### Added

- **A seventh Academy course: Carpentry**, taught at a new Carpentry Bench standing beside the
  Lecture Hall in the Academy interior (the Lectern's own six study-progress fields were all
  already spoken for by the other six courses). Study Carpentry, then sit the Final Exam to earn
  the Carpentry Graduate perk and unlock four new furniture blueprints, all filed under the
  existing Construction › Furniture tab: **Clothes Rack**, **Wicker Chair**, **Coat Rack**, and
  **Wardrobe**.
- **Clothes Rack — one-motion Dress/Undress.** Hang gear in the rack's own storage, then use
  "Dress" to equip everything it's holding that you have a free equipment slot for; use "Undress"
  to hang your entire current outfit back on the rack at once (skips anything the rack has no room
  for, leaving it equipped rather than losing it). This is a real equip/dequip shortcut, not a
  reskinned drag-and-drop container — it calls the same game code the character-portrait UI uses
  to actually equip gear.
- Wicker Chair, Coat Rack, and Wardrobe are plain new furniture — a seat, a small coat-and-hat
  storage post, and a bigger closed storage cabinet than the vanilla Shelf.

## [1.58.2] — 2026-08-16

### Fixed

- **Killing a guard now actually summons Captain Sterling — previously it summoned nobody.**
  Owner report: "I killed a guard and the captain and the other two guards just stood there for
  2 hours." Root-caused by reading the actual Encounter JSON, not guessed: `cmcStatCaptainSummoned`
  (the stat that sends Sterling after you) is written ONLY by Thorne's or Corrin's
  `PlayerDemoralizedEffects` — i.e. only when the PLAYER LOSES a fight to one of them while being
  chased. `cmcStatGuardsSummoned` (the "whole Watch has converged" signal, the only stat that
  actually builds Sterling a response duty for the no-leniency Converge fight) was set only by Guard
  Vane independently catching up to the player herself. Neither stat was ever touched by
  `EnemyDefeatedEffects` (a KILL) on any guard's own Encounter — winning a fight against a guard,
  including killing them outright, raised Village Crime and marked that guard down/dead, but told
  Captain Sterling and every other guard nothing. If the guard you killed was the only one who could
  ever have summoned him (losing to Thorne/Corrin) and Vane never independently caught you, Sterling
  had no path to ever respond, for any amount of elapsed time. Fixed by adding a
  `cmcStatGuardsSummoned` StatChanges entry to `EnemyDefeatedEffects` on Thorne's, Corrin's, and
  Vane's own guard-fight Encounters — a kill now calls the Captain in for real, routed straight to
  the no-leniency Converge fight (matching the mod's own "let the whole Watch converge on you" line),
  the same as if Vane herself had raised the alarm.
- **The other guards standing nearby also had no reason to react the instant a kill actually raised
  Crime.** `GuardWitnessPatch`'s immediate duty-recheck (added in an earlier pass so a witnessing
  guard doesn't wait out a up-to-15-in-game-minute natural tick before reacting) only ever fired from
  `EncounterPopup.StartEncounter`'s postfix — the moment a fight STARTS, before any kill has resolved
  and before Crime has actually crossed the Banished threshold. A guard standing right there when the
  fight began, then, had already run its one immediate re-check against the OLD Crime value and had
  no reason to check again once the kill pushed Crime over 60. `GuardOutcomePatch.OnGuardDefeated`
  now also calls `GuardWitnessPatch.ReportIncident` the instant a kill resolves, so every guard
  co-located with the fight re-evaluates their own chase duty immediately instead of waiting for the
  next natural tick.

## [1.58.0] — 2026-08-15

### Added

- **Guards can now actually catch you.** Follow-up to 1.57.2's pursuit investigation, which found
  the previously-suspected "leaky border" theory was wrong (the territory is fully closed, one exit,
  already locked). The REAL cause, confirmed by graphing every connection in `WorldMap/MapNodes.json`:
  a chasing guard moves at *exactly* the player's own speed — one environment node per DTP tick, the
  same rate the player themselves travels at — and the territory contains three overlapping 4-node
  cycles (Village Path/Highland Pines/Pine Trail/Hunters Crossing; Village Path/Highland Pines/Mossy
  Clearing/Clay Shoal; Clay Shoal/Mossy Clearing/Foraging Forest/Marsh Hollow). Classical
  pursuit-evasion theory says a single pursuer at equal speed can never force a catch on a cycle like
  that — the evader just keeps circling — which is exactly what a player being chased by Iris Vane
  alone at night could always do, forever, no matter how long the chase went on. New
  `GuardChaseUrgencyPatch` gives any guard actively performing a chase duty one bonus pursuit step
  every few seconds of real time, on top of their normal tick-driven movement — a genuine speed edge
  over the player that breaks the cycle-evasion problem regardless of which loop you run around.
  Guards not actively chasing (patrolling, standing warden, already fighting) are completely
  unaffected.
- **The Village Inn's hearth never actually goes cold.** The vanilla Fireplace dropped into the
  Inn interior drains its fuel like anywhere else and, at empty, transforms into an extinguished
  cold hearth that needs to be manually re-fed and re-lit. The Inn is meant to feel staffed and
  maintained, so `InnFireplacePatch` now tops the fire back off to full the moment its fuel drops
  to 20% while the player is inside, and revives an already-extinguished hearth back to lit on the
  same check — covering both the normal per-tick drain and the case where the fireplace goes cold
  during a long real-world absence from the Inn (a stale save's catch-up simulation can run the
  burnout before the mod's own poll gets a turn).

### Fixed

- **A built River Bridge now always opens the Village Path crossing.** Root-caused via
  `Documentation/Retrospectives/river-bridge.md`: the East connection was gated by two independent
  axes in `WorldMap/MapNodes.json` — `GateConditions` (River Bridge built, or the Village Pathfinder
  trait) opened it, but a separate `LockConditions` StatThreshold force-locked it again whenever
  Village Crime reached the Banished band (60+), regardless of the bridge. A single guard attack
  (+35 Crime the instant the fight starts, win or lose) plus one more encounter was enough to cross
  60 and strand a player outside the village with the bridge fully built. That `LockConditions`
  entry is removed — the River Bridge (or the Pathfinder trait) is now the sole gate on this
  crossing. Village Crime is unchanged otherwise: guard pursuit, Watch reactions, arrest, and jail
  time still work exactly as before, and killing a guard still pins Crime at 100.

---

## [1.57.2] — 2026-08-15

### Changed

- **The 1.57.0 Town Watch rebalance didn't actually make fights longer.** Player report,
  confirmed against a live LogOutput.log: guards were still routing in a handful of hits despite
  1.57.0 raising every guard's Blood pool and armor. Root cause, traced through the vanilla combat
  code (`EncounterPopup.GenerateWoundSeverity`/`InGameEncounter.ModifyEnemyValues`) and the shared
  `Combat_ BTHuntsman` wound table all four guards use: every landed hit drains **Morale 1-2x
  faster than Blood** (Minor wound: Blood -9 / Morale -17; Medium: Blood -17 / Morale -34; Serious:
  Blood -34 / Morale -34), and Morale — left untouched by 1.57.0 — was only 23%-79% the size of
  the newly-buffed Blood pools (Old Corrin was the worst case: 60 Morale against a 260 Blood pool,
  routable in as few as 2 hits). Since a guard's `Morale.OnZeroEncounterResult` is `EnemyEscaped`
  (rout) and `Blood.OnZeroEncounterResult` is `EnemyDefeated` (kill), and Morale always hit zero
  first, the 1.57.0 Blood/armor buff was never actually being tested — every fight ended as a
  Morale-rout long before Blood mattered. Raising Blood without also raising Morale had, if
  anything, made this worse (Old Corrin's Morale/Blood ratio fell from 37.5% pre-1.57.0 to 23%
  post-1.57.0).
- **Fix: raised `Morale.MaxValue` on all five guard combat Encounter assets** (Thorne, Corrin,
  Vane, Sterling, and Sterling's Converge/full-Watch-fight variant) to roughly 109-110% of each
  guard's own Blood pool — Thorne 150 → 210, Old Corrin 60 → 285, Vane 90 → 240, Sterling/Converge
  100 → 350. Blood, armor, damage, and `Morale.OnZeroEncounterResult` (still `EnemyEscaped`) are
  all untouched — a beaten guard still routs rather than dies in the large majority of fights, per
  the existing design intent, but it now takes roughly twice as many hits to get there under
  typical (Minor/Medium-severity) combat, and Blood/armor finally come under real pressure in a
  fight instead of being cosmetic. Sized deliberately so that taking down all four guards is a
  harder overall fight than a single vanilla Wolf Pack encounter (Blood 400, no Morale/rout
  mechanic at all) — Sterling alone now has 320 Blood + 350 Morale (670 combined), and the full
  Watch's combined total across all four guards is well over 3x the Wolf Pack's single-pool
  endurance.
- Investigated a companion report ("guards never seem to overtake me") — traced the actual
  `MoveDutyAction`/`InGameNPC` movement code and confirmed pursuit mechanics are working as coded
  (live per-tick re-targeting, no stale destinations, the territory boundary is fully closed with
  only one exit and it's already locked while Banished). No code change made here yet — root cause
  is more likely the travel distance from the guards' single Town Square patrol post to wherever
  the player is when Banished triggers, combined with the day/night shift split leaving only 1-2
  of 3 chase-capable guards active at a time. Needs a fresh in-game reproduction to pin down further.

---

## [1.57.1] — 2026-08-15

### Fixed

- **Attacking Captain Sterling directly at his post, then declining all three "think better of it"
  chances, never actually led to an arrest.** His own Attack button (`Agent_GuardSterling.json`)
  pointed at the same lenient encounter (`cmcEncounterGuardSterling`) no matter how many chances
  had been burned — the forced arrest only existed on a separate summon-response duty
  (`Duty_SterlingForceArrest`) that additionally required `cmcStatCaptainSummoned`, a stat only set
  by *losing a fight to Thorne or Corrin* while they're chasing you. A player who engages guards
  directly at their posts (rather than being chased down) never sets that stat, so the "next time he
  reaches the player the arrest is forced" promise (`GuardOutcomePatch`'s own log line) could never
  be kept — Sterling would offer the identical choice forever. His Attack button is now two
  mutually-exclusive `Interactions` entries gated on `cmcStatSterlingEscapeCount`: fewer than 3
  chances used still opens the lenient encounter; 3 of 3 used opens `cmcEncounterSterlingArrest`
  directly, forcing the arrest with no further escape option. The chase-summon route
  (`Duty_SterlingForceArrest`, armed by losing to Thorne/Corrin) is unchanged and still works as its
  own independent path. `GuardOutcomePatch`'s diagnostic log line updated to describe both paths
  accurately.

---

## [1.57.0] — 2026-08-15

### Changed

- **The Town Watch was too easy to beat down.** Player report: a fresh, unequipped character
  attacked and killed all four guards outright, took wounds along the way, but never felt seriously
  threatened. Thorne, Corrin, and Vane all had their Blood pool raised (110/160/130 -> 190/260/220),
  their armor more than doubled (Torso 10 -> 22, limbs 5 -> 12), and their base Damage roll
  increased (25-75 -> 35-95), with a matching bump to how much their own Melee Skill scales that
  damage. **Captain Sterling never dealt any damage at all** — every one of his `EnemyActions`
  carried `DoesNotAttack: true` as part of the 2026-08-12 non-lethal leniency redesign, which
  correctly kept him harmless while *offering* the "attack or think better of it" choice, but also
  left him harmless if the player actually chose to attack, or if the whole Watch converged and
  forced a fight. He is now the toughest fight in the Watch when either of those happens — highest
  Blood (140 -> 320), heaviest armor (Torso 35, limbs 18), hardest-hitting (Damage 0-0 -> 45-110),
  and his wound table now applies real Bruising (90/180/320 by severity) alongside Fear, matching
  the fix already shipped for the other three guards in 1.55.0. His forced-arrest asset
  (`cmcEncounterSterlingArrest`, the "third refusal" surrender) is untouched — declining his offer,
  or being taken in on the third refusal, is still unhurt by design; only actually fighting him is
  now dangerous.
- **Jail sentences didn't reflect what you actually did.** Every arrest used the same crime/8
  formula regardless of cause, floored at 1 day — and because a single guard kill already maxes
  Village Crime to its 100-point ceiling, that formula could never tell "killed one guard" apart
  from "killed three"; both just hit the existing 8-day cap. New hidden counter
  `cmcStatGuardKillsPending` tracks guards killed since the player's last full sentence
  (`GuardOutcomePatch`, incremented at the same moment a kill is confirmed); `JailPatch` now floors
  every arrest at **3 days minimum**, and whenever a kill is outstanding, sentences at **7 days per
  guard killed** instead of the crime formula (2 kills already reaches the unchanged 8-day cap, so
  no change was needed to the jail cell door's 8 pre-built "N more days" DAs). The counter resets to
  0 once that sentence is served in full, alongside Village Crime. Killing all four guards still
  bypasses jail entirely, unchanged from 1.53.0 — with nobody left standing to make the arrest, the
  Inn Keeper's confession dialog remains the only way to clear a Watch-wiped record.

---

## [1.56.0] — 2026-08-15

### Changed

- **AdvancedCopperTools (ACT) is now a hard dependency.** The River Bridge, Copper Bed Frame, and
  Market Stall's Copper Pantry all build from ACT items (`advanced_copper_tools_copper_nails` /
  `advanced_copper_tools_metal_sheet`) referenced directly in their construction requirements, with
  no fallback. Without ACT installed those references never resolve — reported as the River Bridge
  environment improvement no longer appearing at all at the River Clearing. Rather than authoring
  CMC-native fallback items for every ACT-flavored feature, `Plugin.cs`'s `[BepInDependency]` on ACT
  is now `HardDependency` instead of `SoftDependency` — Community Mod Chest will not load without
  Advanced Copper Tools also installed. **This is a breaking change for any existing CMC-only
  install.**

---

## [1.55.0] — 2026-08-15

### Fixed

- **Fighting a Town Watch guard (Thorne, Corrin, or Vane) landed hits but never felt like real
  damage.** Their per-round wound tables were copied from vanilla's Wolf encounter, which only
  raises Fear (a psychological "makes you run away" stat) on a landed hit — no physical
  consequence at all. That matched vanilla wildlife design, but a human guard's spear should hurt.
  Every wound tier (Minor/Medium/Serious, both of each guard's attack variants) now also applies
  `Bruising` — the same real vanilla physical-injury stat this mod already uses for Captain
  Sterling's arrest sequence — scaled to the wound's severity (Minor 68, Medium 136, Serious 272,
  out of Bruising's 0-400 range; a Serious hit now crosses into the "Seriously Bruised" status).
  Fear is unchanged and still rises alongside it — a guard fight is still meant to be frightening,
  it's just no longer *only* frightening.
- **Guard wounds left nothing in your inventory either** — vanilla ships a complete set of wound
  items (Bruise, Abrasion, Minor Laceration, Puncture — each causing Pain until you Clean, and in
  some cases Stitch, it away) that no vanilla encounter actually spawns; the same asset set is used
  for fall damage but was never wired into combat. Every guard wound tier now drops the matching
  item on top of the Bruising hit above — a light Abrasion/Laceration/Puncture on a Minor hit,
  scaling up to a stacked pair of them on a Serious one — reusing existing vanilla items and their
  existing Clean/Stitch treatment actions; nothing new was added to the game.

---

## [1.54.3] — 2026-08-15

### Fixed

- **A killed Town Watch guard's body disappeared, but not until ~30 in-game minutes after the
  kill.** The despawn was driven only by `GuardOutcomePatch`'s 5-second poll reading the
  `cmcNpcStatGuardKilled` marker, and the engine writes that marker inside a *time-costed*
  end-of-encounter action performed after the player presses Continue — so the body lingered until
  game time advanced. `GuardOutcomePatch` now also subscribes to `GameManager.OnEncounterEnemyDefeated`,
  which fires the instant the guard's Blood hits zero (before that time-costed action), and removes
  her body + arms her Jail cooldown immediately. A routed guard fires the separate "escaped" event,
  so she is untouched. The 5-second poll stays as a save/load reconciliation backstop.

---

## [1.54.2] — 2026-08-15

### Fixed

- **A killed Town Watch guard's body was removed correctly, then respawned within seconds.**
  `GuardOutcomePatch`'s 5-second poll despawned the killed guard's NPC (`CheckKilledGuards`)
  *before* arming her respawn cooldown, so by the time the cooldown-arming code tried to read
  her `cmcNpcStatGuardDowned` marker off the live NPC, the NPC was already gone — the cooldown
  never armed, `GuardSpawnPatch` never saw her as suppressed, and she took up her post again on
  the very next 1-second arrival poll. The despawn and the cooldown-arming read now happen in the
  correct order within the same poll tick.
- **A guard who was routed first and then killed on a later attack never got flagged as killed at
  all**, reproducing the same instant-respawn bug through a different path — the kill-detection
  logic originally only ran the first time a guard went down, so a guard already down from an
  earlier rout (the mod's own advertised common outcome — "guards break and flee... actually
  killing one takes real determination") skipped it entirely on a later kill. Kill detection now
  runs on every poll, independent of whether the guard was already marked down.

### Changed

- **A killed guard's cooldown is now 7 days, distinct from a routed guard's full season, and she
  returns through the Village Jail instead of her old post** (owner request). Breaking a guard's
  morale (a rout) is unchanged — still a full season, still back at her own post. Killing one
  outright now reads as a heavier but faster-resolving consequence, tied to the Jail: a new
  per-guard hidden marker (`cmcStatGuardKilledFlag*`) tracks whether her current absence came
  from a kill, redirects her first placement after the cooldown to the Village Jail (reachable
  any time via its own "Step into the Jail" action, not only while under arrest), and blocks
  `GuardSpawnPatch`'s restore path from resurrecting her at her old post using her stale
  pre-kill `CurrentSaveData` entry (a load/new-game snapshot never refreshed mid-session) before
  she is actually due back. Known tradeoff, not fixed: shortening a killed guard's cooldown to 7
  days can shrink or close the window in which all four guards are simultaneously down, which
  gates the Inn Keeper confession/pardon path — see `GuardOutcomePatch`'s class doc.

## [1.54.1] — 2026-08-15

### Fixed

- **A killed Town Watch guard no longer stays standing on the board.** Previously, both a kill
  (Blood hit zero) and a rout (Morale hit zero) only ever wrote the same shared
  `cmcNpcStatGuardDowned` marker, so a "killed" guard's own encounter text ("You have killed a
  guard of the village Watch") was contradicted by her card remaining fully visible and
  interactable at her post. Each guard's `EnemyDefeatedEffects` now also sets a new, kill-only
  `cmcNpcStatGuardKilled` marker; `GuardOutcomePatch` polls for it and despawns the guard's NPC
  the same way vanilla removes one for a `DeleteNPC` card action, while leaving a merely-routed
  guard exactly as before. The season-long return to duty is unaffected either way — a killed
  guard's post is simply empty until she is back.

## [1.54.0] — 2026-08-15

### Added

- **The Weaver's Climbing Rope epilogue quest.** Once her regular seven-errand chain is complete,
  talking to her once more offers a standalone final conversation (does not touch or reopen the
  existing quest-chain script): she teaches the **Climbing Rope** blueprint (Rope ×4 + Plank ×1 →
  Climbing Rope). Build a **Climbing Rope** `CardType 10` environment improvement at the vanilla
  **Greenfalls** location (consumes the item) to permanently open the Sett Warren ↔ Greenfalls
  path in both directions — previously a one-way, dead-end exit. The connection is locked from
  both sides (travel DA stripped, cliff "too high") via `WorldMap/MapNodes.json` `ConnectionGates`
  (`Edge` granularity, `ImprovementBuilt` condition) until the rope is anchored, mirroring the
  existing River Bridge pattern. The Climbing Rope item and the placed improvement both use the
  vanilla Rope sprite as a placeholder pending custom art.

## [1.53.0] — 2026-08-15

### Changed

- **Village Reputation no longer starts near-full.** The civic score used to sum flat weights
  for the four core structures + Market Stall milestone (Miller 25, Weaver 25, Well 15, Bridge
  15, Market Stall 20) that added up to the full 100-point ceiling on construction alone, with
  the six villager-errand flags contributing nothing extra. A fresh save with the Village
  Founder perk equipped — which instantly completes 4 of 6 errand flags and spawns Miller's and
  Weaver's cottages on first visit — read as "full reputation" almost immediately. Reputation is
  now split into two 50-point buckets, construction and errands, and BOTH must be fully complete
  to reach the 100 ceiling; neither alone can carry the total past halfway.
- **Beating the entire Town Watch no longer auto-clears Village Crime.** Previously, the instant
  all four guards were simultaneously down, Village Crime silently reset to 0 in the same tick —
  which undid the crime penalty for an actual guard kill (guard `EnemyDefeatedEffects` already
  carried a steep Village Crime hit; the auto-pardon erased it before it meant anything). Clearing
  your name now requires a follow-up conversation: once all four guards are down and Village Crime
  is still above 0, the Inn Keeper has a new dialog warning the player about the ramifications of
  the violence and inviting them to sit with what they did; the conversation itself is what resets
  Village Crime, not defeating the guards alone.

## [1.52.0] — 2026-08-14

### Changed

- **Kit-perk overlap with HomesteadPerks resolved by content separation, not runtime dedup.**
  1.51.0's `HomesteadPerksCompatPatch` (hide CMC's copies from `PerkTabGroup.ContainedPerks`
  when HomesteadPerks is installed) only ever controlled the *available* perk list — it had no
  effect on a character profile that already had CMC's perk UIDs saved as *equipped* (see
  `Documentation/Retrospectives/CMC-HSP-compat.md`), so duplicate kit perks could still show up
  twice in Equipped Perks. Replaced with a simpler, structurally conflict-free split: CMC now
  ships only two of its original nine kit perks — **Founders Kit** (renamed from "Homestead")
  and **Rain Cistern Kit** — while the standalone HomesteadPerks mod remains the sole home for
  all nine (Founders/Homestead, Cabin, Mud Hut, Log Bed, Furnace, Forge, Oven, Rain Cistern,
  Tanning Pit) under its own `hsp*`-prefixed UIDs. With no shared UIDs and no identically-named
  perks between the two mods' remaining offering, there is nothing left to hide or dedup at
  runtime — `HomesteadPerksCompatPatch.cs` is removed, along with the `homestead_perks` soft
  BepInDependency it existed for.
- **"Homestead" perk renamed to "Founders Kit"** (`traits_perk_homestead` UniqueID unchanged, so
  existing character saves keep the perk). The granted item (`cmchomesteadkit`) is renamed to
  match; its contents (cabin kit, two rain cistern kits, building materials) are unchanged.

### Removed

- **Seven standalone starter building-kit perks removed from CMC**: Cabin Kit, Mud Hut Kit, Log
  Bed Kit, Furnace Kit, Forge Kit, Oven Kit, Tanning Pit Kit (and their exclusive granted items).
  These were a straight recreation of the abandoned Better Perks Buildings mod and now live only
  in the standalone, dependency-free **HomesteadPerks** mod. **Rain Cistern Kit is kept** in CMC
  as its own perk — a player can take it alongside Founders Kit to end up with more than the two
  cisterns the Founders Kit bundle already grants. Existing characters with a removed perk UID
  already equipped keep whatever they already placed; the perk simply won't be offered again.

## [1.51.0] — 2026-08-14

### Added

- **Compatibility with the standalone HomesteadPerks mod.** HomesteadPerks packages CMC's
  Homestead trait and its eight Better-Perk-Buildings-recreation perks (Cabin/Mud Hut/Log
  Bed/Furnace/Forge/Oven/Rain Cistern/Tanning Pit Kit) on their own, for players who want the
  placeable-structure kits without the rest of the village content. If both mods are
  installed, CMC now detects HomesteadPerks (by its BepInEx plugin GUID) and hides its own
  nine copies of those perks from character creation, so only HomesteadPerks' set is offered
  — no duplicate "Cabin Kit" entries, and no load-order dependency between the two mods. CMC's
  own perks are untouched and fully functional when HomesteadPerks is not installed.

## [1.50.3] — 2026-08-14

### Fixed

- **Weaver, Apothecary, InnKeeper, and Professor Copper Chests now actually appear.** The
  1.46.0 per-resident chest expansion generalized `Patcher/CopperChestPatch.cs` and shipped
  all 5 chest location cards and their GameStat trackers, but only the Miller's interior
  environment card (`CMC_MillerCottageInterior.json`) was ever given the matching
  `DefaultEnvCardDrops` entry that spawns its chest onto the board. The other 4 interior
  environment cards (`CMC_WeaverCottageInterior.json`, `CMC_ApothecaryCabinInterior.json`,
  `CMC_InnInterior.json`, `CMC_AcademyInterior.json`) never had the equivalent entry added,
  so those chests could never spawn — confirmed by a 2026-08-13 playthrough report ("only
  the miller has a chest"). Each now drops its resident's chest on first visit, same as the
  Miller's. Independent per-NPC theft/heat counters were already correctly wired in C# and
  should now be testable across all 5 residents.

## [1.50.2] — 2026-08-14

### Internal

- Village Flax, Rye, and Turnroot fields are now marked with vanilla Partner-NPC harvest
  duties (`PartnerDuty_HarvestFlax`/`HarvestRye`/`HarvestTurnroot`), matching the existing
  vanilla flax field's own wiring. JSON-only, no new C#. Not yet verified in-game with a
  recruited Partner — not advertised as a feature until confirmed.

## [1.50.1] — 2026-08-14

### Fixed

- **Village Reputation now actually appears on the Mental tab of the detailed stats screen.**
  The old `GameSourceModify/Mental.json` patch could never work: `StatListTab` assets are
  gameplay-scene objects that aren't loaded during the menu-time data load, so the framework
  logged `GameSourceModify: no object found for 'Mental'` on every start and the stat stayed
  untabbed. Replaced with `Patcher/StatTabInjectionPatch.cs`, which appends the stat to the
  Mental tab at game boot (idempotent, re-applied each run in case the scene asset reloads).
- **Professor/Apothecary no longer log a scary `ResolveRefs failed … can never spawn/schedule`
  warning during the main menu.** The schedulers tick once before game data loads, so the first
  resolve attempt always missed and cried wolf; that expected menu-time miss is now a silent
  Debug breadcrumb, and the warning only fires if references are still unresolved while a game
  is actually running (a real failure).

## [1.50.0] — 2026-08-14

### Changed

- **Homestead trait reworked to a single "Homestead Kit" card.** The perk previously granted
  the Cabin Kit, two Rain Cistern Kits, and raw materials directly into starting inventory —
  all of it counted against the 4000 Encumbrance cap at once, which forced the material bundle
  down to a token 3 Plank/2 Mud Brick/2 Stone/3 Copper Nails/1 Rope in [1.48.4] to avoid
  immobilizing the player on spawn. Now the perk grants one portable Homestead Kit (600 weight);
  using **Place** unpacks the Cabin Kit, two Rain Cistern Kits, and a real starting stockpile —
  30 Planks, 30 Mud Bricks, 50 Stones, 20 Heavy Stones, 15 Tree Logs, 20 Clay, 10 Rope, 50
  Copper Nails — on the spot, so none of it has to be carried cross-country first (Heavy Stone
  and Tree Log are only viable at these quantities because they spawn on the ground instead of
  going straight into starting inventory).

## [1.49.0] — 2026-08-14

### Added

- **Six starter building-kit perks** — a working recreation of the abandoned *Better Perks
  Buildings* mod, requested by players after that mod stopped working. Each perk grants a
  one-time-placeable kit at character creation; carry it with you and use **Place** to raise
  the building wherever you like:
  - **Cabin Kit** (60 Suns) — the Homestead cabin kit, plus a Broom blueprint
  - **Mud Hut Kit** (40 Suns) — a new mud hut kit, plus a Broom blueprint
  - **Log Bed Kit** (15 Suns)
  - **Furnace Kit** (20 Suns)
  - **Forge Kit** (25 Suns)
  - **Oven Kit** (15 Suns)
  - **Rain Cistern Kit** (10 Suns) — the Homestead rain cistern kit
  - **Tanning Pit Kit** (15 Suns)
- Six new kit items (Mud Hut, Log Bed, Furnace, Forge, Oven, Tanning Pit) that transform into the vanilla
  structures on placement, matching the existing Homestead kit behavior. All perks sit in the
  Situational tab alongside Homestead. English + Chinese localization included.

---

## [1.48.4] — 2026-08-13

### Fixed

- **The Homestead trait made a character instantly "Too encumbered to move," so a Homestead
  run could only ever settle at spawn.** The [1.46.6] fix reduced the Cabin Kit alone from
  6000 to 2500 weight, but never checked the perk's OTHER granted items against the same
  4000 Encumbrance cap. The two Rain Cistern Kits (1200 each) plus the Cabin Kit (2500) summed
  to 4900 on their own — over the cap before the player touched anything else — and the
  perk's separate raw-material bundle (`EquippedCardsWarpData`, placed directly into carried
  inventory at character creation) added a further ~72,000 weight on top of that (30 Heavy
  Stones at 750 each = 22,500; 10 Tree Logs at 3000 each = 30,000; plus Planks, Mud Bricks,
  Stones, Copper Nails, Rope), roughly 19× the entire carry cap. `CMC_HomesteadCabinKit.json`
  reduced to `1200.0`, `CMC_HomesteadRainCisternKit.json` reduced to `400.0` (all three kits
  together now total 2000 — well under cap with room for ordinary gear). `Perk_Homestead.json`'s
  material bundle cut to 3 Planks, 2 Mud Bricks, 2 Stones, 3 Copper Nails, 1 Rope (Heavy Stone
  and Tree Log removed entirely — even a single Tree Log alone is 3000 weight, 75% of the whole
  cap, and cannot be included at any quantity without reintroducing the same bug). Total granted
  weight is now 3440, leaving margin for starting clothes. `PerkDescription` (JSON + both
  localization CSVs) updated to describe the smaller bundle honestly. Placeholder values, not a
  final balance pass — same caveat as the original Cabin Kit fix.

### Changed

- **Framework hardening (CSFFModFramework 2.22.3): `Api.ActionRouter`'s wrapped-action coroutine
  can no longer strand the game in a permanent action-lock.** If the game's own action coroutine
  threw partway through (any dialog, drag, or DismantleAction with a framework `AfterWrapped`
  handler registered), the exception propagated out of `RunWrapped` uncaught — the same failure
  shape already fixed once for `GameManager.ChangeEnvironment`
  (`ChangeEnvironmentCrashGuard`, [1.44.4]/fwk 2.20.6): the coroutine never reached the point
  where the game clears `RootAction`, so `PerformingAction` stayed true forever and every later
  action showed "I can't do two things at once..." with no recovery short of quitting. This is
  general defensive hardening, not a confirmed fix for the specific "spoke with the Professor and
  could no longer go anywhere" report — no reproduction of that softlock exists in the log
  reviewed this session (which instead shows the River Bridge trait, hammer-slot fix, and
  interior/wildlife fixes below all working correctly this same run). If the "can't go anywhere"
  softlock recurs, please leave the game running and send a fresh `LogOutput.log` — an `Error`-
  level `[ActionRouter]` line will now name the exact action/card involved if this is the cause.

### Verified (re-confirmed against current source + a fresh player log, no changes needed)

- **River Bridge trait + hand-build hammer slot** ([1.46.4]–[1.46.6], [1.46.7]'s portal fix):
  confirmed still correct in source and confirmed firing successfully in a player's
  `LogOutput.log` from this same build (`RiverBridgeUnlock` log line shows the bridge
  force-unlocked, auto-completed for the Village Pathfinder perk, and the player successfully
  reaching the Village on foot afterward). The legacy `ForgeHammer` GUID has stayed swapped to
  `ToolOrWeapon_Hammer_Metal` in both `Imp_RiverBridge.json` and
  `Bp_CMC_IronFishingRodFittings.json`.
- **Bears/wolves spawning inside village interiors, and interiors briefly showing the outdoor
  Village card on first entry** ([1.46.6]): `EncounterGuards/CMC_InteriorsNoWildlife.json` and
  `InteriorEnvSaveDataPatch` both confirmed present and firing in the same player log
  (`[InteriorEnvSaveData] pre-created/verified EnvironmentsData for 7/7 interior environment(s)`).
- **Teleporting to the Village via the Portal Hub before building the bridge, with no way back
  across the river:** traced the WorldMap graph — `cmcEnvVillagePath`'s westbound connection
  back to River Clearing is a plain, ungated `Connections` entry (only the River-Clearing-side
  EASTBOUND entry DA is gated by `ConnectionGates`/`HideTravelDA`), so it stays walkable on foot
  regardless of bridge state; separately, `PortalService`'s `CloneNodeHasOwnExit` fix ([1.46.7])
  is confirmed still granting `cmcEnvVillage` its own hub-exit return card in the same session
  log (`[PortalService] hub travel handler registered: world 2 'Village' → 'cmcEnvVillage'`,
  not skipped the way ACT/H&F's own-exit clone nodes are). No remaining chicken-and-egg lock
  found in the current source.

## [1.48.3] — 2026-08-13

### Fixed

- **Six village map locations renamed to stop colliding or being confusable with vanilla location names.**
  `cmcEnvHighGrove`/`cmcLocHighGrove` and `cmcEnvMossyClearing`/`cmcLocMossyClearing` are clones of the vanilla
  environments `Env_GrovePine_HighGrove` and `Env_ClearingOak_MossyClearing`, and had kept those environments' exact
  vanilla display names ("High Grove", "Mossy Clearing") — indistinguishable in menus, logs, and player conversation
  from the real vanilla locations of the same name. Renamed to **Highland Pines** and **Moss-Grown Clearing**. A
  follow-up pass also renamed four more nodes that echoed a *different* vanilla location's name closely enough to be
  confusable, even though the full text wasn't identical: **Clay Flats** → **Clay Shoal** (vanilla has "Clay Banks"),
  **Marsh Hollow** → **Sodden Hollow** (vanilla has "Heather Marshes"/"River Marsh"), **Deer Meadow** → **Stillwater
  Meadow** (its own clone source is vanilla "Deer Grove"), **Badger Warren** → **Sett Warren** (its own clone source is
  vanilla "Badger Hill"). All six: English + Chinese localization, `WorldMap/MapNodes.json` `DisplayName`, plus the
  North Snow Drift's description/action text which named Deer Meadow by name. UIDs, connections, and terrain are
  unchanged — this is a display-text-only fix. The other six village map nodes were audited against the full vanilla
  `CardName` table and confirmed to not collide with or closely echo any vanilla location.

## [1.48.2] — 2026-08-12

### Fixed

- **Village building interiors (Inn, Academy, Miller's Cottage, Weaver's Cottage, Village Hall,
  Apothecary's Cabin, Jail Cell) now self-declare as indoor environments** (`"tag_EnvIndoors"` in
  `CardTagsWarpData`). These 7 environments previously shipped with no `CardTags` at all, which
  silently broke Sirus23_Mod_Collection's Owl-companion "don't follow indoors" check (and any
  other future indoor-aware mod logic) for every one of them — the Owl kept following straight
  into the Inn/Academy/etc. despite that mod's fix. `CardData/Environment/*Interior*.json`,
  `CardData/Environment/CMC_JailCell.json`.

## [1.48.1] — 2026-08-12

### Changed

- **Village Reputation is now a visible Mental attribute in the character menu**, instead of a
  hidden tracked-only stat. It appears alongside Morale, Stress, Connection, etc. in the Mental
  tab (`GameSourceModify/Mental.json` appends `cmcStatVillageReputation` into the vanilla tab's
  `ContainedStats`, so no vanilla file is overridden). The Village Hall's Notice Board mirror and
  every existing threshold/gate are unchanged — this only adds a second place to see the number.

## [1.48.0] — 2026-08-12

### Changed

- **Captain Reeve Sterling no longer deals damage to the player under any circumstance.** Every
  one of his combat actions — blocking, grappling, holding — stops short of drawing his sword on
  you; he can no longer wound you in any encounter, including one you start yourself by using the
  Attack button on him.
- **Being caught alone by Sterling now opens with a choice instead of a forced fight.** When
  Thorne or Corrin has beaten you and sent for the Captain, and he catches up with you himself,
  the encounter offers **Attack the captain** or **Think better of it**. Choosing to think better
  of it ends the confrontation with no fight at all — you walk, and Sterling simply resumes
  pursuing you.
  - You get **three** such chances, tracked by a hidden counter.
  - The third refusal spends the leniency: the next time Sterling reaches you, the encounter opens
    with no escape option at all, and he takes you into custody without a fight and without
    hurting you.
  - You still wake up in the **Village Jail** afterward, serving the same crime-based sentence as
    before — but with **no Bruising** this time, since no blow was ever struck.
- **The whole-Watch encounter, when Guard Iris Vane's alarm brings every guard down on you at
  once, keeps its existing stakes.** That encounter opens straight into the fight with no
  leniency offer. Sterling still deals no damage in it, so you can fight through and beat him same
  as any other guard — the "best all four guards and the village drops the charges" reward path
  is unchanged.

## [1.47.1] — 2026-08-12

### Fixed

- **Allied companions (the vanilla Partner, and any future NPCAgent flagged
  `AlliedWithPlayer`) can now follow the player through the Village Inn and Village Academy
  doors, in both directions.** Building interiors were never nodes in the WorldMap graph, so
  vanilla's own follow mechanism (`NPCDuty` + `MoveDutyAction`'s A* pathfinding) could never
  route a companion through either door no matter how the duty was tuned. `PartnerIndoorFollowPatch`
  bypasses pathfinding for this boundary and directly relocates any allied NPC that was
  standing with the player in the village the moment they step through — the same direct
  `GameManager.MoveNPC` mechanism CMC's own resident schedulers (Professor, Miller, Weaver,
  Apothecary, Inn Keeper) already use for their own interior comings and goings. A companion
  who wasn't with the player is left where it was rather than teleported in.

## [1.47.0] — 2026-08-12

### Changed

- **Village Renown and Village Crime merged into a single Village Reputation stat.** The Village
  Hall's Notice Board, the Town Board's standing text, the Inn Keeper's News dialog, and the Clay
  Beads/Stone Mace reveal thresholds now all read a new signed `cmcStatVillageReputation` stat
  (civic score minus your own Village Crime notoriety), instead of the old unsigned
  `cmcStatVillageRenown`. All existing thresholds (25/50/75/100) mean the same thing as before in
  the common case (zero crime) — the only new behavior is that high Village Crime can now visibly
  suppress your civic standing, including delaying the Clay Beads/Stone Mace reveals and the
  Market Stall's full-reputation bonus if crime is high enough to offset it.
  `cmcStatVillageCrime` itself, `VillageCrimePatch`, and the entire Guards/Jail/Banishment system
  are functionally unchanged — Crime remains its own independent 0-100 incident ledger, now with
  a second, read-only consumer.
  - New Town Board notice for a net-negative Village Reputation (previously no board text
    acknowledged that state at all).
  - Fixed a latent bug where the Town Board's status line silently vanished if a read-succeeded
    sentinel check (`>= 0f`) ever saw a legitimately negative value — not reachable before this
    merge, since Renown could never go negative.
  - Fixed a mismatched threshold: the Town Board's prose tier text switched to its top tier at
    80% while the board's own declarative bands switched at 75% — both now agree at 75%.

## [1.46.7] — 2026-08-11

### Fixed

- **Academy lectern self-heal could permanently latch onto the wrong duplicate instance.**
  `GraduatePerkPatch.CheckAcademyBackfill` used a single-match `FindLiveCard` to locate the
  `cmcAcademyLectern` on the board, backfilled it, then latched a "already backfilled" flag that
  never retries — if it resolved an orphan/empty duplicate instead of the instance the player
  actually sees, the visible lectern stayed stuck at 0% forever (graduate perks still granted).
  Mirrors `AcademyPatch.FindAllLiveLecternCards`: now reconciles every live `cmcAcademyLectern`
  instance found on the board, and only latches the flag after at least one instance was written.
- **Five NPC spawn/schedule patchers failed silently when an Agent UID couldn't resolve.**
  `CottageResidentSpawnPatch`, `GuardSpawnPatch`, `AshPartnerSpawnPatch`, `ProfessorSchedulePatch`,
  and `ApothecarySchedulePatch` all polled a `GetFromID` result with zero log output on failure — a
  renamed/mistyped UID or a JSON load failure meant the affected NPC silently never spawned, with
  no diagnostic trail. Each now emits a one-shot `LogWarning` naming the unresolved UID(s).

### Changed

- Demoted 6 shipped `LogInfo` diagnostic call sites (`CompanionFollowDiagnostics`,
  `AcademyPatch.DumpLecternInstanceIdentity` + duplicate-count log) to `LogDebug` — these were
  temporary investigation logging left at Info level past their investigation, contrary to
  §Mod Logging Norms. No behavior change; log-volume hygiene only.

---

## [1.46.6] — 2026-08-11

### Fixed

- **The Homestead trait's cabin kit was too heavy to carry.** `CardData/Item/CMC_HomesteadCabinKit.json`
  shipped `ObjectWeight: 6000.0`, well above the vanilla starting-character Encumbrance cap of 4000 —
  holding the kit alone triggered "Too encumbered to move," so a Homestead run could only ever settle
  at spawn. Reduced to `2500.0` (a placeholder value, not a final balance pass — well below the cap,
  heaviest vanilla carryable is 3500).
- **The River Bridge blueprint slot could never accept a player-held forge hammer.** Both
  `CardData/EnvImprovement/Imp_RiverBridge.json`'s construction stage and
  `CardData/Blueprint/Bp_CMC_IronFishingRodFittings.json` referenced the vanilla legacy `ForgeHammer`
  card (`e118b8cd90f14b048aab78a0d37e8f61`), which self-transforms into `ToolOrWeapon_Hammer_Metal`
  the instant it spawns — no player could ever be holding the legacy card, and blueprint slots match
  by exact `CardData` reference, not name. Swapped both references to the modern hammer GUID
  (`2914e01d9af26f24d92ff61389fb0195`).
- **Bears and wolves could spawn inside village interior buildings.**
  `EncounterGuards/CMC_VillageNoWildlife.json` only guarded outdoor village-area environments and
  carried one dead UID (`cmcEnvVillageHall`, unused by any card); none of the seven enterable
  interiors were covered. Removed the dead UID and added a new
  `EncounterGuards/CMC_InteriorsNoWildlife.json` suppressing wildlife encounters in all seven
  interiors (Inn, Academy, Apothecary Cabin, Miller's Cottage, Weaver's Cottage, Village Hall, Jail
  Cell).
- **A village interior's very first visit could show the outdoor Village card instead of the
  interior's own furnishings.** The seven interior environments had no `EnvironmentsData` entry
  pre-created, so `GameManager.ChangeEnvironment`'s gate failed on first entry and the engine
  re-dropped the outdoor Village's `UniqueOnBoard` card onto the interior board. New
  `Patcher/InteriorEnvSaveDataPatch.cs` pre-creates the save-data entry for all seven interior
  environments at run start.
- **Village Pathfinder's River Bridge auto-build failed silently on an outdated framework, and could
  miss a bridge that had already spawned before the auto-complete queue armed.**
  `RiverBridgeUnlockPatch` now probes for the three `CardUtil` helpers it needs at startup and logs
  one clear, actionable `LogError` (naming the required framework version, 2.17.0+) instead of a
  swallowed `MissingMethodException` buried as a warning. It also now scans the current board for an
  already-spawned, incomplete bridge improvement card and completes it directly for perk holders,
  covering the case where a save is loaded while the player is already standing at River Clearing
  (the auto-complete queue only catches bridges spawned *after* it arms at `OnGMInitialized`).

### Changed

- **Hardening: the seven interior location cards now set `AlwaysUpdate: false`.** All seven
  (`CMC_{Inn,Academy,ApothecaryCabin,MillerCottage,WeaverCottage,VillageHall}InteriorLocation.json`,
  `CMC_JailCellLocation.json`) had shipped with `AlwaysUpdate: true`, a documented rule violation for
  CT4/CT8 environment cards. This is a rule-compliance correction, not a resolution of the reported
  professor travel softlock — that root cause remains unconfirmed.

## [1.46.5] — 2026-08-09

### Fixed

- **The River Bridge improvement could stay permanently invisible at River Clearing, most often on
  old saves, for any player without the Village Pathfinder perk.** The slot only renders once the
  engine spawns a ghost card for it, which only happens once the CT10's own `CardUnlockConditions`
  discovery gate (`CardsOnBoard: "HasPlank"`) evaluates true *while the player is standing at River
  Clearing* — being listed in the CT8's `EnvironmentImprovements` array (via
  `InjectImprovementInto.json`) is necessary but not sufficient. Nothing guaranteed a returning
  player was holding a Plank the moment the engine's periodic unlock scan ran, so the gate could
  stay unsatisfied indefinitely and the bridge would never appear, regardless of the mod working
  correctly otherwise. `VillagePathfinderBridgePatch` already bypassed this exact gate with
  `CardUtil.ForceUnlockCard`, but only for Village Pathfinder perk holders. Renamed to
  `RiverBridgeUnlockPatch` and now bypasses the gate for every player, every run start — the
  bridge slot always appears the first time River Clearing is visited that run, construction
  materials still required as normal. The perk's existing auto-build shortcut (marks it fully
  constructed, no materials needed) is unchanged and still perk-exclusive.

## [1.46.4] — 2026-08-09

### Fixed

- **The Inn Keeper was still missing from the Village Inn after the [1.46.2] fix.** That fix
  corrected the *NPC's* environment but not her *board card's* — the two are separate `EnvID`s, and
  vanilla `GameManager.MoveNPC` only carries the card across inside a block it skips entirely when
  the NPC's own environment already matches the target. The 1.46.2 boot-time reconcile runs from
  `InitializeStatsAndActions`, which is *before* vanilla builds her board card, so it moved an NPC
  that had no card yet: her environment was corrected and saved, her card was left in the orphaned
  `cmcInnInterior_cmcEnvVillage`, and from then on both reconcile paths saw a matching NPC
  environment and stood down every load. Verified against the owner's `LogOutput.log`: she restores
  from save, the player walks into `cmcInnInterior` with an exact environment match, and the mod
  logs nothing further — no error, no warning, no reconcile.
  `InnKeeperSpawnPatch` now runs a board-presence check while the player is standing in the Inn that
  asserts the *card's* environment, not just the NPC's, and repairs either — relocating a
  left-behind card (by clearing the NPC's environment first, so vanilla's early-return can't skip
  the card relocation) and requesting a new one via `AssignOrCreateNPCCards` if she has none at all.
  The check logs the state it observes on every change, is capped at five repair attempts per
  session, and is inert once she is present. Affected saves repair themselves the next time the
  player enters the Inn. **Note:** as with [1.46.2], loose items left in the Inn before the 1.44.x
  environment flip are in that same orphaned environment and are not recovered.

## [1.46.3] — 2026-08-09

### Fixed

- **The Village and Village Farm could show vanilla river-flood warnings ("Moderate Overflow" /
  "High Overflow") even though neither sits on an actual river tile.** `WaterLevel`/
  `WaterLevelVisible` is a vanilla *global* stat (one value for the whole save, driven by
  `RainValue`/weather) rather than something tied to a specific environment, so it displays
  and applies everywhere the player stands, including the Village's cloned `Green Glade`
  environment. Vanilla's own countermeasure is the `Levee` structure — a `CardType: 13`
  invisible helper (`LeveeInvisible`) carrying a passive effect that clamps `WaterLevelVisible`
  by -7665 (fully suppressing the overflow status and its "flooded" travel penalty) while the
  player is physically standing in the same environment. `WorldMap/MapNodes.json` now force-drops
  this vanilla Levee marker onto both `cmcEnvVillage` (Town Square, alongside the Inn/Academy/Jail)
  and `cmcEnvVillageFarm` (alongside the seasonal crop fields) via the existing `ConditionalDrops`
  mechanism, so both are permanently flood-protected without any player action required.

## [1.46.2] — 2026-08-09

### Fixed

- **The Inn Keeper was missing from the Village Inn in saves that had met her before 1.44.x.**
  Confirmed from a live save, not inferred: her saved environment key was
  `cmcInnInterior_cmcEnvVillage` while the player now travels into the bare `cmcInnInterior`.
  An NPC's location persists as a *string key*, and for an `InstancedEnvironment: true` env that
  key carries the parent chain. 1.44.x correctly flipped all six village interiors to
  `InstancedEnvironment: false` (to fix the entering-an-interior softlock), which changed the key
  the player travels into — but nothing rewrote keys already written into saves. On load the
  Keeper was restored into that now-orphaned environment, vanilla's `AssignOrCreateNPCCards` built
  her board card *there*, and she became permanently invisible: no error, no warning, and the
  once-a-second arrival check saw her as "already spawned" and stood down every time the player
  walked in. She was the only village NPC affected — the Professor, Apothecary, Miller and Weaver
  are all moved by their own schedulers, which rebuild the environment ID from the card each time
  and so silently self-corrected after the flip; the Keeper never moves, so nothing ever
  rewrote hers. `InnKeeperSpawnPatch` now reconciles her environment against the Inn interior card
  itself in two places — immediately after the save restore (before her board card is built) and
  again whenever the player is standing in the Inn — using vanilla `GameManager.MoveNPC`, which
  also carries an already-created card across. Affected saves repair themselves on the next load;
  the check is inert once the keys agree. **Note:** items left in the Inn before the 1.44.x flip
  are in that same orphaned environment and are not recovered by this fix.
- **Miller's and Weaver's Cottage operation blueprints now require Copper Nuggets specifically**, not
  any metal type. All nine station recipes (Grind Rye/Wheat/Acorn into Flour, Mill Logs into Planks,
  Process Hemp/Flax/Nettle into Fiber, Weave Large Cloth, Weave Rope) referenced the generic
  `MetalNugget` GUID with no metal-type gate, so Tin or Iron nuggets satisfied the requirement even
  though every recipe's flavor text says "for a fee in copper" / "keeping a copper nugget for the
  work." Added `Special4: {Active:true, FloatValue:100, MaxValue:100}` (Copper's SD4 value) to each
  recipe's nugget requirement slot, matching the vanilla `Bp_CommissionCopperNuggets` pattern.

## [1.46.1] — 2026-08-09

### Fixed

- **Confirmed the Inn Account currency deposit ("drag Salt/Nuggets onto the Inn Counter") works
  correctly** — the `TEMP DIAGNOSTIC (2026-08-08)` logging added while chasing an owner report that
  "dragging copper nuggets does nothing" caught a real play session (`LogOutput.log`, 2026-08-09) with
  three successful deposits (balance 0→50→100, with a silent `Purchase Meal` draw in between —
  everything self-consistent). No code defect found; the diagnostic `LogInfo` calls in
  `Patcher/InnPatch.cs` are demoted back to `LogDebug` per CLAUDE.md §Debugging Discipline.
- **Not yet confirmed**: the identical deposit CI on the Academy tuition account
  (`Patcher/AcademyPatch.cs`) and the Academy Lecture Hall course-progress reconciler (`[1.45.2]`)
  were never exercised in that session — their diagnostics remain armed at `LogInfo` until a session
  actually tests them.

## [1.46.0] — 2026-08-09

### Added

- **Copper Chests for all five village NPCs.** The Copper Chest — the weekly-accruing container that
  is simultaneously a merchant's savings, their spending power, and a burglary target — was previously
  the Miller's alone (1.38.0, `Village_Master_Plan.md` §10.8.3.7's "prototype it on one cottage first"
  step). It now ships for the **Weaver** (`cmcCopperChestWeaver`, in her cottage interior), the
  **Apothecary** (`cmcCopperChestApothecary`, in her cabin interior), the **Inn Keeper**
  (`cmcCopperChestInnKeeper`, inside the Inn) and the **Professor** (`cmcCopperChestProfessor`, inside
  the Academy), each with its own Sell CI, its own "Search for valuables" theft DA, and its own
  independent theft-heat and accrual-day trackers — five chests never share one counter, so robbing
  the Miller does not raise your risk at the Academy.
- **Per-NPC wealth tiering** (§10.8.3.3, placeholder values pending the tuning pass). Inn Keeper: 500
  salt-value ceiling, 5 Salt/week, and the most varied goods (acorn flatbread, firm cheese, dried meat).
  Miller and Weaver: 300, 3 Salt/week. Apothecary and Professor: 180, 2 Salt/week, but rarer goods drawn
  from their own already-curated pools — healer's moss and old growth bark for her, spirit mushrooms and
  nettle leaves for him. No new items were invented for any chest.
- **Three different accrual mechanisms, one per NPC's existing plumbing** (§10.8.3.3), so no new spawn
  mechanism was introduced anywhere. The Weaver's weekly satchel restock was **retargeted** onto her
  chest exactly as the Miller's was — not duplicated (R6); her `WeaverWeeklyRestock` action is now
  unfired. The Apothecary gets a parallel weekly drop inside her own schedule patch, which already owns
  her poll. The Inn Keeper and Professor get genuinely new weekly ticks, independent of — and not
  replacing — his 4-day pantry restock and the Professor's per-node forage, which are unchanged.
- Each chest is that NPC's **personal** savings. `cmcInnCounter`'s Inn account and `cmcAcademyLectern`'s
  tuition account are untouched and remain separate pools, as §10.8.3.2 requires.

### Fixed

- **The Miller's Copper Chest rendered as a broken/blank image in-game.** Its `CardImageWarpData` was
  `"ChestPlaced"` — a plausible-looking but non-existent sprite name, which resolves to nothing silently
  (no error, no log; root CLAUDE.md §Sprites/Images). It now uses `"Copper_Chest"`, backed by a real PNG
  shipped in this mod at `Resource/Picture/Copper_Chest.png`. All four new chests use the same sprite.
  Because CMC only soft-depends on Advanced Copper Tools, the PNG is a byte-identical copy carried by
  CMC itself rather than a reference into ACT, so the chests render correctly whether or not ACT is
  installed.

### Changed

- `CopperChestPatch` was generalised from three hardcoded Miller constants to a per-resident config
  array; the three action handlers are now registered once per chest, dispatching on each chest's own
  card UID. Its "is the owner standing right here" instant-catch check is now self-contained — it
  previously delegated to a cottage-resident-only helper that knew nothing about the Apothecary, Inn
  Keeper or Professor and would have silently reported "nobody home" (never an instant catch) for all
  three of them.
- Removed the now-unreachable satchel-restock branch from `CottageResidentSpawnPatch`: with both cottage
  residents on chests it could no longer run, and leaving a dormant `RestockActionId` field behind would
  have made the double-drop regression R6 warns about a one-line mistake away.

## [1.45.5] — 2026-08-09

### Fixed

- **Weaver, Miller, and Professor errand "thanks" dialog could never fire, so quest progress never advanced and the village boards stayed frozen on the initial ask forever.** Owner-reported: after delivering the requested materials (dried flax stems, wheat grain, etc.), talking to the NPC again never acknowledged the delivery and the Weaver's/Miller's/Professor's Board kept showing the same unfulfilled errand. Root cause: `DialogScene.GetStartingLine` opens on the first `StartingPoint:true` scene line (in array order) whose conditions pass — and each NPC's generic greeting (`*Talk_Start` / `ProfessorGreeting_Menu`) is essentially unconditioned, so it was listed *before* the conditioned `QuestThanksN` lines in `SceneLinesWarpData`. The generic greeting always won the first-match check, so the Thanks lines (and, for the Professor, several other gated greetings — `GiftValedictorian`, `GiftFirstPass`, `GiftTrust`, `TrustHigh`/`TrustMid`, the seasonal lines) were unreachable dead code; none of their `StatModifications` (incrementing `*QuestChain`, resetting `*QuestArmed`, granting trust/blueprints) ever ran. Reordered `CMC_WeaverTalk.json`, `CMC_MillerTalk.json`, and `CMC_ProfessorGreeting.json` so every conditioned `StartingPoint:true` line is checked before the generic fallback greeting, per the documented "most-restrictive first, unconditioned fallback last" rule — no other wiring changed (dialog branches reference each other by UID, not array position).

## [1.45.4] — 2026-08-09

### Fixed

- **Ash's boar-hunt trail card never actually left the village, despite the log repeatedly saying it had faded.** Owner-reported: "Ash's Trail" (`cmcAshBoarTrail`) stayed on the board indefinitely once the hunt resolved. The cleanup step wrote the trail card's `SpoilageTime` stat directly to 0 via reflection — a write that bypasses the game's own per-tick durability processing, which is the only place `HasActionOnZero`/`OnZero` (the destroy trigger) actually gets evaluated. The stat sat at 0 forever, the card was never removed, and the once-every-2-seconds poll kept re-finding it and re-logging "Ash's trail has faded from the village." `AshBoarHuntPatch` now hands the zeroed trail card to the same CT13 invisible-helper transform already used to remove the tracking card at hunt-start (`BeastTracksCardInvisible` — its own active `SpoilageTime` the real tick loop does evaluate), so the card is actually destroyed instead of just reporting that it was.

## [1.45.3] — 2026-08-09

### Fixed

- **Inn Keeper could fail to spawn inside the Village Inn.** Owner-reported: the Keeper never appeared. `InnKeeperSpawnPatch`'s once-a-second arrival check matched the player's current environment against the cached `cmcInnInterior` card by raw object reference (`ReferenceEquals`) — fragile against any path that ends up with two distinct `CardData` instances for the same UID (this install also runs a third-party ModLoader). The check (and the matching "is this NPC already spawned" check) now compares by `UniqueID` string instead, and failure paths that were previously completely silent now log a one-time warning. Root cause not confirmed from the log alone (it showed no `[InnKeeperSpawnPatch]` activity at all this session) — unverified in-game; diagnostic logging is in place to pinpoint the cause on the next test if this doesn't resolve it.

## [1.45.2] — 2026-08-09

### Fixed

- **Academy Lecture Hall could show 0% progress on a course whose degree you'd already earned.** Owner-reported: after finishing all six courses over many play sessions, every "Study ..." tooltip read "You have already completed this course" (the graduate perk was genuinely held) while the progress bar showed 0%. The lectern's own progress stat had desynced from the perk it's supposed to track — root cause unconfirmed, but the graduate perk is already the authoritative "done" signal used everywhere else (the "already completed" gate, the course-gated blueprint unlocks), so `AcademyPatch` now reconciles the lectern's displayed progress to match it: while standing in the Academy, any course whose perk is held gets its progress stat force-set to max if it reads below that. Self-healing against future desyncs from any cause, not just a one-time repair.
- **Academy course-gated blueprints (Sawmill, Grinding Mill, Copper Sheet, Iron Fishing Rod, copper/iron armor, Forge/Workshop) could get silently re-locked on every reload of a save that had already earned them.** `AcademyCourseService`'s run-start gating pass ran on `GameManager.OnGMInitialized`, which can fire before `InRunAddedPerks` finishes restoring from the save — the pass would find no graduate perks held yet and re-hide the reward blueprints. A one-shot recheck 5 seconds after boot now re-runs the same gating pass once perks have had time to settle.

## [1.45.1] — 2026-08-08

### Fixed

- **Ash's boar hunt no longer logs spurious "may not be loaded" warnings.** The hunt-movement poll treated `SpawnService.Spawn`'s return value as a spawn-success signal, but that call returns null on success in the current game version (the card is placed as a side effect). The trail and tracking-boar cards were always spawning correctly; the code now verifies placement by re-querying the board, so the state advances on the spawning tick and the warning only fires on a genuine failure. No player-visible behavior change to the hunt itself.

## [1.45.0] — 2026-08-08

### Added
- **(1.45.0) Captain Sterling now gives a one-time warning the first time you're caught, and the
  Watch reacts to a Wanted player it can see.** Talk to him (a new "Talk" option) once
  `cmcStatVillageCrime` first reaches the Suspected band and he delivers a single formal warning
  line, latched by a new hidden `cmcStatSterlingWarningGiven` marker so it never repeats. From the
  Wanted band onward (25-59 crime, short of the Banished threshold that arms a chase) any guard who
  shares your current environment reacts without fighting: their own Suspicion rises (feeding the
  existing per-guard detection tuning), and talking to Sterling in that band gets a harder,
  visibly hostile line instead of his usual greeting. Neither reaction is an Encounter or a chase —
  pursuit still only arms at Banished (60+).

### Changed
- **(1.45.0) The four Town Watch guards now have three distinct jobs instead of one identical
  hunt-and-fight duty each.** Captain Sterling drops his own local chase and only moves when
  summoned — sword drawn, not spear, and the player he catches is left Bruised on the way to the
  cell; he is now the only guard whose fight ends in a jail teleport. Guard Thorne and Guard Corrin
  relocate from their separate river/farm beats to a shared day-shift (05:00–20:00) Town Square
  patrol; their local territory-gated chase is unchanged, but beating the player no longer jails them
  directly — they hold the player in place and send for the Captain instead. Guard Vane drops the
  village-territory restriction entirely (the one guard who now crosses the river) and, once she
  reaches the player, calls the rest of the Watch in rather than fighting herself; her day-hiding gate
  now also lets her manifest outside her usual night window once summoned into a hunt. Two new
  hidden player stats (`cmcStatGuardsSummoned`, `cmcStatCaptainSummoned`) carry the summon state
  between guards across the multi-hour walk this can take — deliberately not `cmcStatArrestPending`,
  which self-clears the same tick it fires. Not fully in-game verified this release; folds into the
  next full guard-system playthrough pass.
- **(1.44.6) The Watch captain is now "Captain Reeve Sterling," not "Captain Reeve Ashdown."**
  The surname read too close to "Ash," the name of the Village Inn's stray cat — renamed to avoid
  player confusion between the two characters. Purely cosmetic: `UniqueID`s, localization keys,
  and file names were all updated in step (`cmcGuardSterlingAgent`, `cmcGuardPerkSterling`,
  `cmcEncounterGuardSterling`, `cmcStatGuardDownDaySterling`), so no save-compatibility impact.

### Fixed
- **(1.45.0) The Jail's Ration Tray and Meager Prison Ration bowl both rendered as blank cards.**
  Same anti-pattern as the Water Jug entry below, caught this time by a full audit-chain sweep
  rather than manual inspection: `CMC_JailRationTray.json`'s `CardImageWarpData` was `"ClayPlate"`
  (the vanilla `ClayPlate` card's own UniqueID, not a sprite name) and `CMC_JailRationFood.json`'s
  was `"ClayBowl"` (same mistake, vanilla `ClayBowl` card). Real sprite names are `"Plate_Clay"` and
  `"Bowl_Clay"` respectively — confirmed against the vanilla `Bp_ClayPlate`/`Bp_ClayBowl` exports,
  and `"Plate_Clay"` is already used correctly elsewhere in this mod (`GlazedClayPlate.json`).
- **(1.45.0) A retry of the village-territory guard-pursuit boundary tag could leave stale,
  destroyed-object tag references stamped on already-tagged map cards.** `GuardDutyPatch.cs`'s
  `EnsureVillageTerritoryTag()` stamped its `CardTag` onto each village node as it walked the list;
  if a later node in the same pass failed to resolve, the shared tag instance was destroyed while
  earlier nodes in that same pass kept holding a reference to it, and every retry created a new tag
  and repeated the same partial-stamp risk without ever clearing the stale one. Rewritten as a true
  two-pass commit: every node is resolved and staged first, and the tag is only written onto any
  card once the entire batch resolves cleanly (with a rollback path if an individual write
  unexpectedly fails mid-commit) — a failed attempt now leaves zero cards holding a reference to the
  discarded tag, so a retry starts clean. Found by `/code-quality`, not observed as a player-facing
  symptom in this release.
- **(1.45.0) Five NPC-spawn paths (Ash's Partner spike, the Town Guards, the Inn Keeper, both
  Cottage residents, and the Professor) had no log output if `CreateNPC` succeeded but the
  follow-up identity lookup in `GameManager.AllNPCs` came back empty** — a silent no-spawn
  indistinguishable from "nothing went wrong." Each of the five `SpawnAndReturn` helpers now logs a
  warning naming the NPC when this happens, so a future framework/game-version change to NPC
  identity semantics is diagnosable instead of invisible. Found by `/code-quality`, not observed as
  a player-facing symptom in this release.
- **(1.45.0) The Jail Cell's Water Jug (`cmcJailWaterJug`) rendered as a blank card.** Its
  `CardImageWarpData` was `"WaterPouch"` — the human-readable name of the vanilla Water Pouch card,
  not its actual sprite name. Confirmed against the vanilla `Bp_WaterPouch` export that the real
  sprite name is `"Waterskin"` (`WaterPouch` appears nowhere as a `CardImageWarpData` value in
  vanilla data). Fixed by pointing the jug at `"Waterskin"`.
- **(1.44.4) The "Exit" action on all 6 village interior doors (Inn, Academy, Apothecary's Cabin,
  Miller's Cottage, Village Hall, Weaver's Cottage) was still invisible after the 1.44.x
  `AlwaysShow` fix below — confirmed in-game (player permanently trapped inside the Miller's
  Cottage interior with no usable action on the door).** Root cause was misdiagnosed the first
  time: `AlwaysShow: true` only bypasses `CanAppear()`; it does **not** bypass
  `CardAction.WillHaveAnEffect()`, and `WillHaveAnEffect()` DOES evaluate `TravelToPreviousEnv`
  (via `WillProduceCards()`, confirmed by decompile) — but only returns true when
  `EnvID.GetPrevEnv()` resolves to a real card. `EnvID.GetPrevEnv()` reads `EnvID.ParentEnvs`,
  which the engine populates **only for `InstancedEnvironment: true` environments**
  (`EnvID(CardData, EnvID, int)` ctor). All 6 interiors are `InstancedEnvironment: false` (correctly
  — see the entry below on why flipping that flag is worse), so `ParentEnvs` is always empty and
  `TravelToPreviousEnv` can never produce a destination for these doors — the Exit button was
  permanently un-renderable, `AlwaysShow` or not. Fixed for real this time by dropping
  `TravelToPreviousEnv` entirely and giving each door's Exit action an explicit `ProducedCards`
  drop of the specific outdoor environment it should return to (`cmcEnvVillage` for five of the
  six; `cmcEnvForagingForest` for the Apothecary's Cabin) — the same explicit-destination pattern
  the Village Jail Cell door already used successfully. Because this doesn't depend on any
  per-session parent-env history, it also un-sticks saves where a player is already trapped inside
  one of these interiors — no `InstancedEnvironment` flag or save data needs to change.
- **(1.44.x) The "Exit" action on all 6 village interior doors (Inn, Academy, Apothecary's Cabin,
  Miller's Cottage, Village Hall, Weaver's Cottage) was invisible, trapping the player inside with no way
  out.** Each door's `Exit` `DismantleAction` relied only on `TravelToPreviousEnv: true` to leave —
  but the game's `WillHaveAnEffect()` visibility gate (which decides whether a `DismantleAction`
  renders as a button at all) does not check `TravelToPreviousEnv`. With `ReceivingCardChanges.
  ModType: 0` and `DaytimeCost: 0` and nothing else set, every one of these six actions evaluated
  to "no effect" and never rendered, leaving the door's action panel completely empty. Fixed by
  adding `"AlwaysShow": true` to each door's Exit action, matching the pattern already used
  correctly by the Village Jail Cell door. **⚠ Superseded by the 1.44.4 entry above — this
  diagnosis was incomplete and the fix did not actually resolve the softlock.**
- **Entering the Inn (and 5 other singular interiors) permanently softlocked the game
  ("I can't do two things at once..." on every subsequent action).** The Academy, Apothecary's
  Cabin, Inn, Miller's Cottage, Village Hall, and Weaver's Cottage interiors were all flagged
  `InstancedEnvironment: true`. That flag makes vanilla's `WorldMapData.AddInstancedEnv` register
  the interior into `CoordsDict` keyed by its (unassigned) `Coords`, which defaults to
  `(0,0,0,0)` for a singular interior with no sibling instances — so the *second* one of these six
  interiors entered in a session collided on that key, threw inside the `ChangeEnvironment`
  coroutine, and left the action-lock permanently held. All 6 are now `InstancedEnvironment: false`,
  matching the Village Jail Cell (which never hit this because it already shipped with the flag
  off). `InstancedEnvironment` is only correct for templates the engine spawns multiple
  independent instances of (e.g. vanilla `Env_Cabin`); these are unique, persistent, named
  locations. A save that already has two of these envs recorded at `(0,0,0,0)` may still crash on
  load — test from a save taken before either was first entered, or a fresh game.

### Added
- **Homestead perk now grants portable Cabin/Rain Cistern kits instead of auto-placing the
  structures.** Previously `Perk_Homestead.json`'s `AddedCardsWarpData` referenced the real CT2
  structure UIDs directly (`7042e3f52e632a2408319de344a3aa0c` Cabin, `536f722edbb5e9e4b959b1f3ad25f648`
  Rain Cistern ×2) — the same mechanism vanilla's `Pk_5_12_CabinStart` uses — which spawns them
  straight onto the starting board, since `GameManager.GetStartingCardsFromArray` falls back to a
  direct board-drop for `CardType.Location` cards passed through a perk's `AddedCards`. Added two
  new CT0 item cards, `CMC_HomesteadCabinKit` and `CMC_HomesteadRainCisternKit` (`DoesNotPile`,
  single-use), each carrying a `Place` `DismantleAction` (`ModType: 2` Transform,
  `SpawnTransformAtSource: true`) that turns the kit in-place into the real structure — the same
  kit-then-transform pattern already used by `CMC_MarketStallKit`/`HuntingStandKit` in this mod.
  The kits land in the player's starting inventory (CT0 items resolve through the same
  `_UseDefaultInventory: true` path the perk's building materials already use) so they can be
  carried to any site before being placed, and each is consumed on placement — one placement per
  kit. `Perk_Homestead.json` now points `AddedCardsWarpData` at the two kit UIDs instead of the
  structure UIDs; description text updated to match.
- **The Village Jail** (Village Guards system, `Village_Master_Plan.md` §10.8.7) — losing to the
  Watch now has somewhere to put you.
  - **Arrest is a real transition, not a flag.** Each guard's Encounter now sets
    `PlayerDemoralizedEffects.MovePlayer = MoveToSpecificArea` at the new Jail Cell with a 1-hour
    `MoveDuration`, so vanilla's own post-encounter travel path carries the player into the cell on
    the same "Continue" click that ends any other fight. No Harmony patch, no coroutine.
  - **A sentence you have to sit out.** Roughly one day per 8 points of Village Crime at the moment
    of capture, floored at 1 day and capped at 8. The cell door's "Walk out" action is hidden
    outright until the sentence reaches 0, and eight read-out actions on the same door show how many
    days are left. Each day served decrements the sentence and pays down the crime score at the same
    8-points-per-day rate; release clears the record outright.
  - **A warden rotation.** Every guard now carries a `Duty_JailWarden` alongside their patrol and
    chase duties, with a fixed non-overlapping shift block each (Sterling 05:00-11:00, Thorne
    11:00-17:00, Corrin 17:00-23:00, Vane 23:00-05:00 — inside her own night window) so the post is
    always rostered. Vanilla's weighted-duty engine does the switching; CMC does not hand-drive it.
    The warden stands on the jail's step in the village square rather than inside the cell: duty
    movement resolves through the WorldMap graph, and the cell is deliberately not a map node.
  - **A rare gap.** Each hour there is an 8% chance the whole Watch leaves the jail step for two
    hours. This is enforced on the duties themselves — a hidden `cmcStatJailWardenGap` countdown
    that fails every warden duty's `DutyConditions` — so the guards genuinely walk off rather than a
    flag merely claiming they did. `cmcStatJailUnguarded` is then read back from what the guards are
    actually doing. The hidden escape tunnel this window exists for is the next bullet.
  - **Daily rations and a starvation safety net (§10.8.7.4, Risk R9 — release-blocking).** A
    "Meager Prison Ration" bowl and a Water Jug are placed in the cell once per in-game day while a
    sentence is being served, each an explicit `ModType:3` Eat/Drink action. Verified against real
    vanilla `GameStat` decay data (not estimated): Weight decays -12 to -23 per 15-minute tick
    depending on its own band (independent of Satiation, contrary to the original plan's
    assumption); Hydration — a *different* stat from Thirst, and the one that actually carries a
    GameOver floor ("Dead of Thirst" at 0) — decays at a flat -1/tick. Across the worst-case 8-day
    sentence the ration keeps Weight net-positive and Hydration net-positive under baseline decay.
    Beyond the ration, a disableable safety net (`EnableJailSafetyNet` config, default on) polls the
    player's actual Weight/Hydration/Satiation every tick and force-tops-up any of them that cross a
    threshold with a wide margin above their floor — the real guarantee against R9, since Weight's
    decay can stack with cold/fever/parasites the ration math alone doesn't model.
  - **Ways to pass the time.** "Meditate" (2-hour, no material effect — an optional Stress tie-in
    was considered and deliberately left out, unresolved design choice) and two flavor actions
    ("Count the stones," building to a 100-count milestone payoff) on the cell's own room card. The
    cell's bed also carries vanilla's own Nap/Sleep actions verbatim, reused as-is rather than
    building a bespoke rest mechanic.
  - **Known gaps, stated plainly.** The Jail and its cell ship with blank placeholder art
    (`CMC_Jail.png`, `CMC_JailCellInterior.png`) — real art drops over the same filenames with no
    JSON change. None of this has been verified in-game.
- **The Hidden Tunnel** (Village Jail escape, `Village_Master_Plan.md` §10.8.8) — a way out of a
  cell sentence besides waiting it out.
  - **Move the bed, find a tunnel.** The cell's bed can be shoved aside and back in place at will,
    swapping in place for a Tunnel card on the same tile (`CardUtil.TransformCardInPlace`, the first
    REPEATED player-driven toggle use of that primitive in this codebase rather than a one-shot
    upgrade/process transform — flagged as risk R10 and requiring its own repeated-toggle,
    across-a-reload verification pass before being trusted).
  - **Digging only works during the same guard-absence window the warden rotation already tracks** —
    no separate detection roll. Each Dig advances a hidden `cmcStatJailTunnelProgress` counter and
    costs 30 minutes; the Dig button is hidden outright (not merely greyed) whenever a guard is
    actually on the step.
  - **Getting caught.** If the Watch returns while the tunnel is still exposed, the tunnel is caved
    back in (progress reset to 0, card force-transformed back to Bed), the sentence grows by 50% of
    its ORIGINAL length, and a small crime penalty is added for the attempt. Re-hiding the tunnel
    before the window closes avoids all of that and keeps whatever progress was already dug.
  - **A finished tunnel** adds a "Crawl through the tunnel" action that clears the remaining
    sentence, drops the player at the Village Path outside the walls, and adds its own (separate,
    harsher-reads) crime bump for "escaped custody" — an escaped convict caught again is not treated
    like a first-time arrest. Recapture-penalty and escape-crime-bump sizes are both open-question
    placeholders (§10.8.10 Q12), not tuned values. Not verified in-game.
- **8 perishable items and placed decor now carry `tag_Preservable`** (Apothecary Healing Potion,
  Herb Paste, Heather Wreath, Woven/Tricolor Wall Hangings, Straw Mat, Garden Trellis, Bedroll). This
  is a vanilla CardTag with no shipped vanilla users; third-party mods that bulk-match spoilage-rate
  effects onto it (e.g. freshness/preservation perk mods) now apply correctly to these items. Ash's
  Trail, Shadow Cat, Inn Cat, and the Academy Lectern were deliberately excluded — their `SpoilageTime`
  channel is either relabeled for a different mechanic (Hunger, a Fishing progress counter) or has a
  zero decay rate, so tagging them would be either semantically wrong or a no-op.
- **The Miller's Copper Chest** (Village Guards system, `Village_Master_Plan.md` §10.8.3) — a
  storage chest inside the Miller's cottage interior that is his savings, his spending power, and
  a burglary target all at once, all read off the one physical inventory.
  - **Weekly accrual** — once the Miller has moved in he adds ~3 Salt and 2 Wheat Flour to the
    chest about once a week, applied the first time you enter the cottage on or after the due day
    (the chest lives in an instanced environment, so it is only reachable while you are standing
    in it). Currency pauses at 300 in value, goods at 10 cards, each independently.
  - **Sell to the Miller** — drag a valuable item onto the chest to sell it, priced on the same
    `ObjectWeight / 10` scale the Market Stall uses. The sale is refused outright, with nothing
    consumed, when the price exceeds what is physically in the chest — so a freshly settled Miller
    genuinely cannot afford an expensive item, and the same Miller weeks later can. He pays from
    his own hoard smallest-coin-first, and overpays rather than blocking a sale he cannot make
    exact change for. No crime cost — this is an ordinary trade.
  - **Search for valuables** — empties the chest to you and rolls for detection: instant catch if
    the Miller is home, otherwise 15% base + 5% per earlier undetected theft this season (heat
    resets on a season change and on being caught) + 20% while Iris Vane's night watch is out.
    Detected costs 10 Village Crime — the **first mechanic in the mod that actually raises that
    stat**. Undetected leaves no record at all.
  - **Miller only this release** — the Weaver, Apothecary, Inn Keeper and Professor chests are a
    later chunk, per the plan's own one-cottage-first sequencing.

- **Village Crime foundation** (Village Guards system, `Village_Master_Plan.md` §10.8.2) — a new
  hidden `cmcStatVillageCrime` stat (0-100), its four bands (Clean / Suspected / Wanted /
  Enemy-Banished), a 1-per-day passive decay for the two middle bands, and the declarative
  banishment travel lock on the Village Path connection gate. The Copper Chest's theft roll (above)
  is the first and so far only thing that raises it (+10 per detected burglary); attack detection
  is a later chunk. Requires framework 2.20.2+ for the travel lock to be read at all.
- **The Town Watch** (Village Guards system, `Village_Master_Plan.md` §10.8.1) — four guards now
  live on the village map and walk their own beats: **Captain Reeve Sterling** holds the village
  square, **Guard Nella Thorne** works the river-bridge junction on the Village Path, **Guard Old
  Corrin** walks the farm and clay-flats round, and **Guard Iris Vane** takes the night watch,
  appearing only between dusk (20:00) and dawn (05:00) and staying out of sight the rest of the
  day. Each guard carries their own hidden Suspicion rating — the tuning knob the later theft and
  attack-detection systems read — with Iris noticeably sharper-eyed than the rest and Corrin the
  slowest to suspect anyone. Portraits are placeholders reusing existing art. Requires framework
  2.20.0+.
- **You can attack the Watch** (Village Guards system, `Village_Master_Plan.md` §10.8.4) — each of
  the four guards now has an **Attack** button on their inspection panel. It opens a confirmation
  first: back out and it costs you nothing, commit and you take **+35 Village Crime the moment the
  fight starts**, win or lose. That is enough on its own to put a clean player in the Wanted band.
  - **Guards usually break and run rather than die.** Each guard has their own fight, and morale
    drains about twice as fast as blood, so a beaten guard normally routs — hurt, alive, and now
    with a very good reason to remember you. Killing one is possible but takes real determination,
    and it pins Village Crime at its 100 ceiling: instantly and permanently Banished.
  - **Each guard fights differently.** Old Corrin gives up early and takes a lot of punishment to
    actually kill; Nella Thorne holds her nerve far longer than anyone else, which makes her the
    one most likely to die if you push. Sterling and Vane sit between them.
  - **A guard standing in the same place when you start a fight notices immediately** rather than
    on their next patrol beat — the witnessing guard's own pursuit check re-runs on the spot
    instead of waiting for the normal patrol tick, and Nella Thorne now acts on the resulting
    Crime score without that lag (see the pursuit entry below).
  - **Guards only.** The Miller, Weaver, Apothecary, Inn Keeper and Professor cannot be attacked;
    proving the pipeline on the Watch first is deliberate.
- **Guard Nella Thorne hunts you once you are Banished** (Village Guards system,
  `Village_Master_Plan.md` §10.8.5) — the first guard pursuit. Once your hidden Village Crime
  reaches **60 or more** (the Enemy/Banished band — two attacks on the Watch will do it), Thorne
  abandons her river beat and walks the map toward wherever you actually are, one node per move,
  re-routing as you run. Stand still and she catches up; when she reaches your location she starts
  a real fight on the spot.
  - **She will not cross the river.** The pursuit only holds while Thorne is standing on
    village-side ground; step onto the vanilla map and she breaks off and heads back to her beat.
    She may follow one node past the boundary before turning around — she is checked when she picks
    her next move, not mid-stride.
  - **Drop back below 60 Crime and she goes back to patrolling**, immediately.
  - Only Thorne pursues for now — the other three guards keep to their beats until this one is
    proven in play. Losing the fight does not yet jail you; the jail is a later chunk.
- **`ForceClearVillageCrime` debug config** — admin override that holds the crime stat at 0, so a
  tester locked out of the village by a future banishment can recover without editing a save.
- **`EnableGuardDiagnostics` debug config** — keeps re-logging each guard's duty weights and the
  engine's own duty-selection reason string while patrol behavior is being tuned.

### Changed
- **The Miller's weekly satchel restock has been RETARGETED onto his Copper Chest, not duplicated
  into it** — his personal carried inventory is no longer topped up every week; his trade stock is
  now his starting inventory plus whatever is in the chest. This is deliberate (the design calls
  for one accrual, not two); a build that kept both would double-drop every week.
- `VillageCrimePatch` gains `ReduceCrime(amount, reason)` — a partial pay-down that, unlike the
  daily decay, deliberately works inside the Banished band, so days served in the jail count.

### Fixed
- **README understated the live Village Crime system.** The Requirements section still claimed
  "nothing in the mod raises the crime stat yet... this currently has no effect in play," which was
  stale and directly contradicted the README's own Town Watch section — attacking a guard (+35),
  killing one (+100, instant Banished), getting caught robbing the Miller's Copper Chest (+10), and
  getting caught mid-dig in the jail escape tunnel all already raise it, on any framework version.
  Only the banishment travel-lock *enforcement* needs framework 2.20.2+; Crime itself was never
  framework-gated. Documentation-only correction, no behavior change (found by `/critical-analysis`).
- **`CMC_JailRationTray` was missing both its localization keys** (`CardName`, `CardDescription`)
  from `SimpEn.csv`/`SimpCn.csv`. English displayed correctly via the JSON `DefaultText` fallback, so
  this was invisible in an English playtest; Chinese players saw raw English. Both rows added with a
  translated Chinese row.

## [1.44.1] — 2026-08-07

### Added
- **Simplified Chinese localization** — `Localization/SimpCn.csv` now covers all 1,771 player-visible
  keys (full parity with `SimpEn.csv`: 0 missing, 0 untranslated, 0 stale). Terminology follows a
  dedicated glossary (`Documentation/Design/Chinese_Glossary_CMC.md`) built from vanilla's own
  Chinese strings and established ACT/WDI/H&F house style, with per-NPC register notes so dialog
  voice (Inn Keeper's warmth, the Professor's formality, etc.) survives translation.

## [1.37.1] — 2026-08-06

### Fixed
- **The Professor no longer forces a throwaway "Go on." click before showing his topic menu.**
  His greeting was still split into two steps — a flavor line with a single "Go on." answer, then
  the real menu — left over from the 1.37.0 consolidation of his seven duplicate entry lines into
  one canonical menu. Every other village NPC (Inn Keeper, Miller, Weaver, Apothecary) already opens
  straight into their topic list; the Professor's greeting now does the same, folding his opening
  line directly into the menu so the first thing you see is real conversation options.

## [1.37.0] — 2026-08-06

### Changed
- **Inn Keeper and Professor dialogue reworked for less repetition and more meaningful conversations.**
  The Professor's greeting used to duplicate his entire ~25-option topic menu across seven separate
  entry lines (default greeting, two trust tiers, four seasonal greetings) — hand-copied text that had
  quietly drifted out of sync with itself, so the "What is this place?" answer only correctly described
  your actual location if you happened to greet him through the one variant that had been fixed. All
  seven now share one canonical topic menu, so every fix and addition applies everywhere at once.
  The Inn Keeper's five seasonal fireside tales (Wolves, Miller's Wager, the Fish Tale, the Mushroom
  Peddler, the Ferryman) were each told across four short, mostly single-line "go on..." exchanges —
  they're now told in two longer, fuller passages apiece.
- **Both NPCs' weekly quest chains got a proper acceptance beat** — accepting a request no longer
  jumps straight to a fade-out; there's now a short, in-character reaction before the conversation
  closes, and the delivery "thank you" lines are longer and more specific about what you brought.

### Added
- **The Professor now reacts, once, the first time you pass each of his six Academy courses** —
  Metallurgy, Herbalism, Medicine, Fishing, Architecture, and Armorer each get their own short,
  subject-specific congratulations the next time you talk to him, instead of only ever seeing a generic
  "how many courses have you finished" summary.
- **Both NPCs now react to village construction milestones.** The Professor comments, once, when the
  Apothecary's Cabin and the Village Hall are finished; the Inn Keeper comments, once each, when the
  Miller's Cottage, the Weaver's Cottage, and the Village Hall are finished, and again when Village
  Renown crosses the halfway and full-renown marks.

## [1.36.2] — 2026-08-06

### Fixed
- **The Inn Keeper, Professor, Miller, Weaver, and Apothecary stopped appearing in the village at all** —
  the game's EA 0.66 update changed the internal method the game uses to create an NPC, and every one of
  this mod's village-resident spawn routines was still calling the old version. Nothing indicated an error
  to the player; the village just stayed empty aside from Ash (who isn't spawned the same way). All five
  residents now spawn correctly again.

## [1.36.1] — 2026-08-06

### Fixed
- **Removed an unfinished, unreleased test companion agent that could unexpectedly appear in your game.** An
  in-progress experiment for a future "Ash as a hands-free companion" feature was accidentally left switched on:
  it spawned a second character also named "Ash," using the same portrait as your tamed Inn cat, with
  placeholder debug buttons visible in its card popup. This was never finished or intended to ship — it's now
  disabled again. Nothing about your existing tamed Ash is affected.
- **README's framework requirement now correctly lists 2.19.0+ for the winter road blockage.** The winter
  snow-drift feature (1.35.0) needs a newer `CSFFModFramework` than the mod's previously-listed minimum; on an
  older framework the roads simply never sealed for winter, with no indication why. Documentation only — no
  behavior change.
- **`Powerful Healing Potion`'s carry weight corrected from 0.5 to 50** — a scale error left it registering as
  nearly weightless compared to every other item in the mod.
- Minor schema cleanup: 3 items' `CardImage` field now uses the correct placeholder object instead of a bare
  sprite name (cosmetic only — the actual artwork was never affected).

## [1.36.0] — 2026-08-06

### Added
- **New starting perk: Homestead (100 Suns).** Start with a Cabin and two Rain Cisterns — all
  fully portable, so you choose where to place them — plus a stockpile of building materials:
  30 Wooden Planks, 30 Mud Bricks, 30 Heavy Stones, 50 Stones, 10 Tree Logs, 50 Treenails, and
  10 Rope. Uses the same vanilla `AddedCards` grant the base game's own "Cabin Start" perk relies
  on, so the Cabin and Cisterns behave exactly like their vanilla counterparts (pick up, carry,
  drop anywhere to place).

## [1.35.0] — 2026-08-05

### Added
- **The Village gets snowed in every winter.** All three roads out of the Village — north to Deer
  Meadow, south to Pine Trail, and east to the Village Farm — now drift over with snow for the
  duration of winter, blocking travel until dug clear. Each drift takes 3 shovel strikes to clear
  (dropping Snow with each strike) and, once cleared, stays open for the rest of that winter — but
  the snow returns to block the road again the following winter. Requires CSFFModFramework 2.19.0
  (new `"Season"` gate-trigger support).

### Fixed
- **Inspecting a Village Hall board could leave the vanilla Time Options ("T" key) menu's description text permanently shrunk/truncated.** The board-status feature below only restored the shared description text object's font sizing on 4 of its 5 vanilla writers — the Time Options screen used the 5th, uninstrumented one, so it inherited the boards' truncated sizing instead of its own. Added the missing patch; Time Options now always renders at its normal size regardless of which board you looked at last.
- **Self-serve drinking from a Rain Cistern had no Care cost at all**, despite being advertised (1.34.1, "at the cost of your bond") as lowering a cat's Care with every automatic drink. The auto-drink code restored Thirst and drained the cistern but never touched the Care stat — a fully cistern-reliant cat's bond never actually degraded. Auto-drink now costs 25 Care per drink, zeroing a fresh cat's Care in about 4 drinks (~9-10 in-game days at the auto-drink trigger threshold), matching the original design. Also corrected the auto-drink trigger threshold in this document: it's Thirst below **20%**, not 30% as previously stated.
- **Telling the Inn Keeper "he wandered off" about Ash could permanently retire that check-in conversation.** Picking that answer while Ash was alive and present at the Inn decremented the same hidden progress stat used to detect a genuine wander-off, without checking whether he was actually gone — since the spawn logic for his replacement never fires while the original cat still exists, the stat could never climb back to the value needed to re-open the conversation. The answer now only fires its stat change when Ash isn't currently on the Inn's board.

## [1.34.1] — 2026-08-03

### Added
- **Village notice boards now show your standing with each villager.** Every Village Hall notice board (Inn Keeper, Miller, Weaver, Apothecary, Professor) now displays a live status readout beneath its notices: your **friendship** with that villager as a descriptive tier plus the raw value in parentheses (e.g. "The Inn Keeper considers you a good friend. (18 / 30)"), and your **quest progress** with them (e.g. "You are working through the Miller's errands. (2 / 3)"). The two **Village Standing** boards likewise show current **Village Renown** and whether the village has been properly introduced. Values update every time you open a board — no more guessing at hidden trackers.
- **Companion cats now self-serve water from a Rain Cistern — at the cost of your bond.** Ash and Shadow each gained a visible **Care** stat (their relationship with you). Whenever a cat's Thirst drops below 20% and a **Rain Cistern with water** is on the same board, it will automatically drink to refill its Thirst — but every self-serve drink lowers its Care a little, because you weren't the one who tended it. Left entirely to the cistern with no personal attention, a cat's Care runs dry after roughly **9 in-game days** and it wanders off (recoverable through its usual return path). **Petting, Feeding, or personally Giving Water** all restore Care, so a hands-on owner keeps a happy cat indefinitely. Implemented as a small tick-driven patch (`AshCatTickPatch.cs`) rather than vanilla's native `DurabilityTransferEffects`, since Care's decay needed to be tied to the auto-drink event itself. An empty cistern gives no water, so a fully neglected cat can still die of thirst on the old ~3-day clock.

### Fixed
- **The "Village Founder" perk no longer fast-forwarded the Miller's grain quest or the Weaver's flax quest.** A prior update moved those two quests onto the shared quest-chain system, but the Village Founder perk's fast-forward list still wrote the old, now-unused quest stats. Equipping the perk now correctly marks both quests as already completed, matching every other village beat it fast-forwards.
- **Completing the boar encounter with Ash did not drop a boar carcass.** The `AshBoarHuntPatch` resolved the fight (transforming Ash back to `cmcInnCat`, setting epilogue stats) but never spawned loot. Now spawns a vanilla `Carcass_BoarCarcass` at the current board (Hunter's Crossing) when the encounter ends in a player victory.
- **Ash the Cat's quest line soft-locked permanently if Ash ever wandered off (starved of food or water).** When Ash's Hunger or Thirst hit zero he was removed from the game, but nothing reset the hidden lost-cat quest stat (`cmcStatLostCat`) or the boar-hunt state (`cmcStatAshBoarHuntSpot`). The stray never respawned (it only reappears while the search is armed), the boar hunt never fired (Ash was gone before his 5-day timer), and the Inn Keeper was stuck forever asking "how's Ash?" with no way to recover. Added a recovery answer to that check-in greeting — telling the Keeper "he wandered off" re-arms the search, so Ash reappears in the Foraging Forest at dusk and can be re-adopted (which also restarts the boar-hunt clock). Fixes existing stuck saves and any future wander-off.
- **Wind effects persisted indoors in the Village Inn, Academy, and other village building interiors.** The "Wind Affinity" passive effect from outdoor grove environments was not suppressed when standing inside the village's closed indoor environments (Inn, Academy, Apothecary Cabin, Miller Cottage, Village Hall, Weaver Cottage). Added an inverse wind resistance modifier (RateModifier -0.5 on the wind affinity stat GUID) to the "Sheltered from the Elements" passive effect already protecting those interiors from rain, sun, and snow — wind effects no longer affect the player indoors.

## [1.33.8] — 2026-07-30

### Fixed
- **Trees never grew back once chopped down, in the Village, Village Farm, Village Path, and by extension every other CMC map location.** Every vanilla explorable location carries "Create Small/Large/Birch Tree" actions that silently re-plant a chopped tree once it's missing from the board. WorldMap/MapNodes.json's `StripLegacyBoardUIDs` (used on Village/Village Farm/Village Path to keep wild Nettle/Clover/Meadowgrass patches from sprouting in the finished settlement) matches and deletes any action that *produces* a stripped card UID — which caught the tree-respawn actions too, since they produce the same tree cards. Added a small env-scoped daily check (`TreeRespawnPatch`) that replaces the missing behavior across all twelve CMC map locations: once per in-game day, while standing in that environment, any of its native tree species missing from the board has a chance to grow back, keyed to the correct species per location (oak groves regrow oak, pine groves regrow pine, etc.).

## [1.33.7] — 2026-07-30

### Fixed
- **Rotten Remains piled up permanently in NPC inventories.** Food NPCs are carrying (deliveries, foraged stock, restock items) spoils the same as it does for the player, but no NPC action ever cleared the resulting Rotten Remains back out. Added a periodic sweep (every 15 in-game minutes) that removes any Rotten Remains found in an NPC's own inventory.

## [1.33.6] — 2026-07-29

### Fixed
- **Every card's Info-tab description text was locked to a shrunken font (worse after 1.33.5).** The Town Hall board sizing patch remembered the description font size from the first card inspected and then force-wrote it — with auto-sizing disabled — onto every non-board card's popup. Because the game's own text box auto-sizes natively, the remembered value was often an already-shrunk computed size, so every card inherited it permanently. The patch now snapshots the complete vanilla text state (auto-sizing flag, size, min/max, overflow) only at the moment a Town Hall board first modifies it, restores that exact state when a non-board card is next inspected, and leaves the text completely untouched otherwise. Only the 7 Town Hall boards' descriptions auto-shrink now.

## [1.33.5] — 2026-07-29

### Fixed
- **Font auto-shrinking meant for the Village Town Hall boards was shrinking every card's hover tooltip in the game.** The board description sizing (added in 1.33.2) is correctly scoped to just the 7 tracked boards, but a second, unrelated patch also force-enabled auto-sizing on the game's single shared hover-tooltip text object — since that object is reused for every card's hover box, the effect applied globally instead of just to the boards. Removed the unrelated global patch; only the Town Hall boards' description text resizes now.

## [1.33.4] — 2026-07-29

### Changed
- **The Village Well now shows its custom art immediately on construction**, instead of the vanilla "Well" sprite (a leftover tropical-island image that never matched the village). Previously the well was built with that vanilla placeholder and only got the intended art if you used the Well Plans sketch's "Re-face the Well" action afterward — but that swap wasn't reliably refreshing the on-screen image. Removed the two-stage indirection entirely: the well card itself now points at the correct art from the start, so there's nothing left to swap. The "Re-face the Well" action and the card it swapped to are gone; the sketch's "Discard the Old Sketch" action still works as before.

## [1.33.3] — 2026-07-29

### Fixed
- **Drawing water from the Village Well only filled a Rain Cistern by about 1% per drag.** The well's "Draw Water" action added a flat 300 units of water to whatever container was dragged onto it — plenty for a Waterskin or Cooking Pot, but a sliver of the Rain Cistern's 21,600-unit capacity. Changed to the same oversized flat quantity (100,000) vanilla water sources (River, Pond, Lake, etc.) use for their own "Fill" action — the game always caps the actual amount added to whatever room is left in the container, so this now tops off any container, including a Cistern, in one drag.

## [1.33.2] — 2026-07-29

### Fixed
- **Lecture Hall description overflowed onto the discipline icons and Study buttons below it.** The card description crammed in a per-subject breakdown of every course's hour length and every degree's unlock effect, making it nearly 2.5x longer than any other location card's description in the mod and long enough to run past the popup's text box. Trimmed to the funding mechanic only; each subject's course length and unlock effect were already restated in that subject's own Study/Final Exam action description, so no information was lost.

## [1.33.1] — 2026-07-29

### Fixed
- **Re-faced Village Well showed a blank card image.** The re-faced well (`cmcimpwellcustom`) pointed at a placeholder sprite that was never replaced with real art. It now reuses the Well Plans artwork (the same stone-well sketch shown on the Inn Keeper's plans) instead of a blank image.

## [1.33.0] — 2026-07-28

### Added
- **Matching Inn/Academy card art for the Miller, the Weaver, and the Apothecary.** Each now shows a dedicated portrait while standing inside the Village Inn (`CMC_Miller_Inn`/`CMC_Weaver_Inn`/`CMC_Alchemist_Inn`) or the Village Academy (`CMC_Miller_Academy`/`CMC_Weaver_Academy`/`CMC_Alchemist_Academy`), falling back to their existing Village portrait everywhere else — the same envCard-driven portrait sync the Professor and Apothecary already used, extended to cover these two new locations.

### Changed
- **The Miller and the Weaver now visit the Inn every evening and the Academy once a week, guaranteed** (previously an independent 13%/12% daily roll for each, so either visit could go missed for a stretch of days). Every evening (18:00–21:00) both residents head to the Inn; each also has one fixed day per week (staggered so they don't both go the same day) for an Academy afternoon (13:00–17:00). Occasional outdoor wandering on non-Academy days is unchanged, still a roll.
- **The Apothecary now also visits the Inn every evening and the Academy once a week.** Her stall hours end at 19:15 as before, but she now stops at the Inn (19:15–21:00) before walking home instead of leaving immediately, and one day a week she spends 13:00–17:00 at the Academy instead of the stall. Both legs share the same instanced-env visit-once-to-unlock prerequisite as her cabin homing, and neither fires while she's actively brewing a healing potion.

## [1.32.1] — 2026-07-28

### Fixed
- **Town Hall board descriptions and the 7 village boards' action visibility now actually work.** Since the Town Hall Boards feature shipped (PR-3, 1.x), the Harmony patch that renders each board's gated status lines and hides its inspection-popup action buttons was silently failing to register at all — a reflection lookup for the game's `InspectionPopup.Setup` method didn't specify which of its two overloads to target, and the resulting ambiguity exception aborted patch setup before any of the four board-related patches applied. Boards affected: Town, Town Construction, Inn Keeper, Miller, Weaver, Apothecary, Professor. No player-facing symptom beyond the boards never showing their intended extra text — the mod otherwise loaded and ran normally, which is why this went unnoticed.

## [1.32.0] — 2026-07-28

### Added
- **Village Well art choice.** Once the Well is built, the Well Plans sketch now offers two one-time, mutually-exclusive options while you stand at the finished well: the existing **Discard the Old Sketch** (keeps the well's original vanilla stonework), or the new **Re-face the Well**, which re-skins the well with custom forest-cut stone art in place of the legacy "Well" sprite (a holdover asset that reads as a tropical-island well rather than a Fantasy Forest one). Both options consume the sketch either way. Implemented via an in-place `CardModel` swap (`WellArtPatch.cs` + `CardUtil.TransformCardInPlace`, never `Object.Destroy`), retargeting the on-board well card to an otherwise-identical structure (`cmcimpwellcustom`) that differs only in its art. Real custom art is pending delivery — ships for now as a blank placeholder PNG.

### Changed
- **Village NPCs now walk their commutes instead of teleporting.** The Professor, the Miller, the Weaver, and the Apothecary previously jumped straight from their current spot to their next scheduled destination in a single hop. They now visibly step across each intermediate map tile along the way (paced at roughly 45 in-game minutes per tile), so a long commute reads as an actual walk rather than a teleport. Purely a presentation change — schedules, destinations, and timing windows are unchanged.

## [1.31.1] — 2026-07-27

### Fixed
- **The Village Hall's "Renown" score, and the Market Stall's 25% sales bonus it unlocks, are now correct no matter where you're standing.** Renown used to be recomputed from a live scan of whatever's physically on the board around you, so the moment you walked away from the Village, Miller's Cottage/Weaver's Cottage/the Well/the River Bridge/the Market Stall would all read as "not built" and Renown would drop — silently resetting the Market Stall's sales bonus back to nothing well before its once-a-day unattended sale ever had a chance to use it. The Village Hall's own Notice Board still looked correct whenever you actually checked it (you have to be standing at the Hall to see it, and everything else is right there with you at that moment too), which is exactly why this went unnoticed. Village construction now counts toward Renown permanently once built, and the Market Stall's sales-milestone bonus is remembered for good the first time it's earned — both regardless of where you currently are.

## [1.31.0] — 2026-07-27

### Added
- **Vanilla trading-value table (`TradingValues.json`)** — NPC trading is no longer full of
  0-cost goods. 653 vanilla items/liquids that shipped with `TradingValue: 0` (planks, rope,
  berries, meat, hides, pottery, tools, carcasses, brews, …) now have prices calibrated to
  vanilla's own scale (bugs/salt 5, herb powders ~17, dried meat 40, flint/metal nugget 250,
  cloth clothing 1500). Deliberately left at 0: truly abundant materials (stones, leaves,
  needles, twigs, grass, snow, water), waste (ash, manure, urine, rotten things), field/planted
  cards, wounds, spirits, and debug/unused cards. Applied via the framework's new declarative
  `TradingValues.json` loader (requires framework ≥ 2.18.0; without it the file is inert and
  trading simply stays vanilla). Full review table: repo `Documentation/TradingValues_Review_2026-07-27.csv`.

### Changed
- **CMC tradeable items priced** — Rare Herb Mixture 200, Powerful Healing Potion 350, Market
  Stall Kit 400, and the 8 apparel pieces (Cloth Coat 1500, Leather Apron 1200, Chaperon 800,
  Leather Sandals 800, Cloth Scarf 600, Straw Hat 200, Foot/Hand Wraps 150). Ash, Shadow, the
  stray-cat cards, and quest items (Well Plans, "Ash, On the Hunt") intentionally stay 0 —
  companions and quest state are not market goods.

## [1.30.1] — 2026-07-27

### Fixed
- **The Well Plans no longer haunt your pack after the well is built.** The dig now consumes the plans as one of the listed build materials, so for new wells the card retires itself. For a well that was already standing before this version, the plans card gains a **Discard the Old Sketch** action that appears only while you stand at the finished well — so the plans can never be thrown away while they are still needed. Before the well is built the card stays protected from trashing, since the Inn Keeper only ever hands it over once.

## [1.30.0] — 2026-07-27

### Changed
- **The Village Well is now a real card on the village board.** It used to be a village-tile improvement: built from the tile's improvements panel, and once finished the well lived inside that panel, where it was awkward to reach and easy to miss. It is now a standard construction blueprint like the cottages and the Village Hall — hold the Inn Keeper's **Well Plans** while at the Village and the blueprint appears in the crafting journal; research it, then build it with the same materials and build time as before. The finished well stands on the village board like any other structure: drag an empty water container onto it to draw water.
- **The Well Plans card now visibly matters.** Keeping it in hand is what reveals the Village Well blueprint — it is no longer an inert keepsake after the Inn Keeper hands it over.

### Notes
- The well keeps its old card identity, so Village Renown, the Village Projects board, the Inn Keeper's well remark and village toast, and Ash's storyline gate all track the new structure unchanged. A well already finished on an existing save is expected to carry over as the new structure card; if it does not appear on the village board after updating, the blueprint offers the rebuild path.

## [1.29.5] — 2026-07-27

### Fixed
- **Players got wet from rain inside every village building.** The Inn, Academy, Apothecary's Cabin, Village Hall, Miller's Cottage, and Weaver's Cottage interiors never granted the vanilla indoor-shelter stat effects (Rain Protection, Sun Protection, Sheltered, hidden snow FX), so standing inside them was mechanically identical to standing outdoors — rain wetness accrued normally. Added the same "Sheltered from the Elements" stat grant vanilla indoor rooms (e.g. the player Cabin) carry, keyed to each building's interior environment card via `Conditions.NotInBackground` (the same mechanism already used by the existing Hearth-Warmed Inn/Academy/Village Hall warmth effects).

## [1.29.4] — 2026-07-28

### Added
- **Miller & Weaver daily schedule.** Both cottages are now enterable ("Enter the Cottage" / "Exit", mirroring the Apothecary's Cabin). Each resident now keeps a daily routine instead of standing at the Village around the clock: home inside their own cottage overnight (22:00–6:00), at the Village the rest of the day, with occasional half-days spent wandering the outdoor map (Village Path/Farm, Foraging Forest, Pine Trail, High Grove, Mossy Clearing), visiting the Inn in the evening, or visiting the Academy in the afternoon. The schedule never moves a resident away mid-conversation, and a resident won't travel to the Inn/Academy/their own cottage interior until you've visited that interior yourself at least once this session (falls back to standing at the Village until then).

## [1.29.3] — 2026-07-27

### Fixed
- **Inn Keeper's fireside tales booted you out after every line.** Each seasonal story arc (the wolves, the mushroom peddler, the fish that got away, the miller's wager, the ghostly ferryman, and the cat's second life) advanced with a step type that meant "end the conversation" instead of "go to the next line," so you had to re-open Talk for every single sentence. All 18 mid-story chapters now flow straight through to their conclusion.
- **NPC "≥ 1" quest/errand gates could never be satisfied — the garlic errand loop.** Any dialog answer, drag-and-drop action, or interaction gated on a stat being "1 or more" used the range `[0.5, 1000000000.0]`, which overflows the game's range math (a fractional lower bound forces a ×10 decimal pass, and `1e9 × 10` overflows a 32-bit integer into a large negative number, making the range impossible to satisfy). This is why delivering **Dried Wild Garlic** to the Inn Keeper never registered even with the correct item in hand — the delivery action was permanently hidden after the first errand. Same defect also silently blocked several "after they moved in" villager interactions (Miller flour hand-off, Weaver/Apothecary pigment steps). Replaced the broken upper bound with a safe large value across all affected gates.

### Changed
- **Village Hall boards cleanup.** The Boards room now opens each villager board directly without a redundant "Read ... Board" action, and the two civic boards were relabeled to **Village Standing** and **Village Projects** to make their roles clearer at a glance.
- **Village Hall boards now render notices as prose instead of button labels.** The Boards room keeps using the existing gated notice data, but the current board entries are now written into the description panel and removed from the action strip so long lines are readable.

## [1.29.2] — 2026-07-23

### Fixed
- **Village Founder + "already completed this course" softlock.** Village Founder granted all
  6 Academy graduate perks directly (bypassing the Lecture Hall), but never backfilled the
  lectern's course-progress stats to match — `AcademyCourseService.HasCourse` reported every
  course as done while the progress bars stayed at 0%, so every "Study ..." button permanently
  showed "You have already completed this course" instead of allowing (harmless, already-passed)
  study sessions to proceed. Fixed by backfilling each course's progress stat to its max hours
  the first time the player stands in the Academy interior.

### Changed
- **Split the Academy grant out of Village Founder into a new standalone "Graduate" character
  perk** (`cmcperkgraduate`, `CharacterPerk/Pk_Graduate.json`, `Patcher/GraduatePerkPatch.cs`).
  Village Founder (3 Stars) now grants only its village-building content (structures, residents,
  NPC first errands); Graduate (3 Stars) grants only the 6 Academy graduate perks + progress
  backfill. The two are independent and can be taken together or apart. `Pk_HigherEducation`
  (already retired) is untouched.

## [1.29.1] — 2026-07-23

### Fixed
- **Portal Hub "Travel to Village" landed at Village Path instead of the Village.**
  `MapMod.json` registered `EnvironmentUID: "cmcEnvVillagePath"` (the road node connecting
  Village Path/High Grove/Clay Flats/Hunter's Crossing), not the actual Village node
  (`cmcEnvVillage`, home to the Inn and Academy). Since `PortalService.RegisterHubTravelHandlers`
  prefers `EnvironmentUID` over `SacredSiteUID` and both fields pointed at the Path variant,
  every "Travel to Village" click from any registered mod's portal hub dropped the player one
  map node short of the village proper. Repointed both fields to `cmcEnvVillage`/`cmcLocVillage`.

## [1.29.0] — 2026-07-23

### Added — Shadow the Cat + Apothecary's Cabin themed finishing stage
- **Shadow the Cat**: a second, wholly independent companion-cat chain (`cmcShadowCat` →
  `cmcShadowCatTamed`, `Patcher/ShadowCatPatch.cs` templated directly on `LostCatPatch.cs`)
  sharing zero UIDs/files/state with Ash. Spawn-eligible in the Foraging Forest between dusk
  and dawn (17:00–06:00) only once Herbalism is graduated (`AcademyCourseService.HasCourse`),
  with no hidden GameStat gating the spawn — the dupe-guard (a live `GameManager.AllCards`
  scan for either cat form) is the sole thing preventing repeat spawns, since the perk check
  stays true forever once earned.
- **Taming is deliberately different from Ash's feed-to-tame arc**: drag any of the three
  vanilla musical instruments (Wooden Flute, Bone Flute, Frame Drum) onto her and she tames —
  none of the three are consumed (`GivenCardChanges: {"ModType": 1}` with every change field
  left at zero/false, the confirmed non-destructive no-op shape, precedented by vanilla
  Fibers.json's "Comb" interaction).
- **Apothecary's Cabin finishing stage** (7th stage, appended to the existing 6-stage vanilla
  backbone): Dried Hemp Flower ×2, Dried Ginseng ×2, Dried Reishi ×2 (all Herbs & Fungi items),
  Dried Nettle Leaves ×3 (vanilla), and one tamed Shadow — `DontSpend: true`, present but never
  destroyed. Ingredient list is a direct, verbatim reuse of the existing Healing Mixture
  blueprint's own rare-herb list, not a new invention.
- **H&F is now a hard dependency for finishing the Apothecary's Cabin** — three of the four
  finishing-stage ingredients have no Community Mod Chest substitute. Documented in README
  (Contents + a new Requirements bullet) and ModInfo `Description` in this same commit; H&F was
  already a declared `SoftDependency` in `Plugin.cs` (BepInEx load order only — this doesn't
  change loadability, only what's needed to finish one specific structure).
- Placeholder white card art for both Shadow forms (`CMC_ShadowCat.png`,
  `CMC_ShadowCatTamed.png`); real art still owed. 15 new SimpEn.csv rows (SimpCn not adopted for
  this file, matching the rest of the village content).
- Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §3.6/§10.7/§7 (Cottage Rework "Independent track")
  updated: Shadow the Cat + Apothecary Cabin stage 6 move from planning-only to CODE BUILT; no
  in-game acid test has run yet (spawn gate, taming CI, stage-6 presence-check with
  `DontSpend: true`, and narrative differentiation from Ash all still need a playtest pass).

## [1.28.0] — 2026-07-23

### Added — Professor Phases P4–P6: event-triggered tasks, a third milestone gift, polish
- **Field Samples** (Phase P4): a new one-shot commission task, offerable once village week 10
  arrives (bridged into a hidden NPCStat, `cmcStatProfFieldSamplesReady`, the same one-shot-latch
  idiom `SyncNarrativeStats` already uses for the river-bridge news flag) — bring 5 fresh
  Billberries, paid in 2 Salt, +6 trust. Discoverable via a new "Any work for me?" dialog branch
  once armed.
- **Applied Herbalism** (Phase P4): a second one-shot commission task, gated on graduating the
  Herbalism course (reuses the existing `cmcStatProfEligibleHerbalism` eligibility stat directly —
  no new subject-bridge needed) — bring 4 Dried Nettle Leaves, paid in 1 Metal Nugget + 2 Salt,
  +8 trust. Also reachable via "Any work for me?".
- Both tasks are genuinely one-shot: each has its own "claimed" NPCStat gate
  (`cmcStatProfFieldSamplesClaimed` / `cmcStatProfAppliedHerbalismClaimed`) so, unlike the weekly
  specimen commission, they never re-offer once completed. Each also contributes +10 to Village
  Renown exactly once (`cmcStatQuestRenownProfFieldSamples` / `cmcStatQuestRenownProfAppliedHerbalism`,
  wired into the existing `VillageRenownPatch` tick alongside the other villagers' one-shot flags).
- **Trust-milestone gift** (Phase P5, the third of the plan's 2–3 milestone gifts — the first two,
  first-degree and Valedictorian, were already shipped in Phase P3): at Trust 80+ the Professor
  gifts 4 Stone Tiles from "the Academy's old teaching kilns," self-disarming via a new
  `cmcStatProfGiftTrust` flag, the same pattern as the existing gift-ladder lines. Chose Stone
  Tiles over an armor-tier reward specifically to avoid the zero-durability-on-spawn risk described
  in CLAUDE.md's GiveCard-postfix-stat-init rule — no CMC code currently initializes durability
  stats on `DroppedCards`-spawned gear, so a shield-tier reward would have handed the player a
  broken 0/80-durability item.
- **Phase P6 polish:** 17 new SimpEn.csv rows for all of the above (SimpCn still not adopted for
  this file, matching the rest of the Professor content); README and ModInfo `Description` updated
  to name the two new tasks and the third gift (docs-honesty pass, same commit).
- Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §10.2 phase log updated: P4/P5/P6 moved from
  UNBUILT to CODE BUILT, pending in-game acid tests (fresh commission offer/turn-in, one-shot
  claimed-guard survives save/reload, trust-gift fires exactly once) — no in-game verification has
  run yet for this batch.

## [1.27.0] — 2026-07-23

### Added — Town Hall Boards now show real quest status (PR-3)
- The six notice boards inside the Village Hall (shipped flavor-only in 1.25.0) now display
  live, gated content driven by each villager's quest chain:
  - Each of the five villager boards (Inn Keeper, Miller, Weaver, Apothecary, Professor) shows
    the villager's currently active errand and a softer hint once it's been offered, a
    non-spoiling teaser for their next likely errand before it's offered (names the theme, never
    the exact item or the reward), a relationship-standing blurb (skipped for the Apothecary, who
    has no trust/friendship stat yet), and a short log of their most recently completed errands
    (auto-expiring after 3 more are finished).
  - The five cross-villager side quests (Tricolor Wall Hanging, Cloth Mask, Painted Plate,
    Quilted Vest, Incense Burner) get a matching teaser on both participating villagers' boards.
  - The Town Board now shows the village's actual Renown standing (in prose, not just a number)
    and which structures are contributing to it.
  - A new seventh board, the **Construction Board**, tracks all 7 village structures (both
    cottages, the Apothecary's Cabin, the Village Hall, the Well, the River Bridge, and the
    Market Stall) as Not Yet Begun / Complete — split out from the Town Board so the Hall's board
    list doesn't overflow past a reasonable length.
- New hidden GameStats (`cmcStatChronicle<Structure>Day`, one per tracked structure) stamp the
  day each structure was completed, written by a new `VillageChroniclePatch` (one new 5s poll).
- **Known simplification:** construction status is two-state (not begun / complete) rather than
  three-state (not begun / under construction / complete) — a cheap "construction in progress"
  signal wasn't available without extra per-blueprint bookkeeping; flagged in the design doc as
  an accepted simplification.
- **Known gap carried over from PR-2:** the Inn Keeper's quest chain is designed for 8 steps but
  only 5 have dedicated offer/hint/teaser content on his board — the remaining 3 (his fireside
  story, Ash's return, and the boar-hunt epilogue) are pre-existing narrative beats that advance
  the same counter without dedicated quest-offer dialog of their own, so the board's content
  caps at step 5 rather than showing a phantom errand with nothing behind it.

## [1.26.0] — 2026-07-23

### Added — Village quest-chain reveal system (PR-2)
- 37 previously-freely-available decor, clothing, and combat item blueprints are now revealed one
  at a time by helping the village's residents, instead of all sitting in the crafting journal
  from a fresh game. Each of the five villagers (Inn Keeper, Miller, Weaver, Apothecary, Professor)
  now runs an independent quest chain: talk to them once a village-week has passed since their
  last errand, accept a fetch request, hand over the item, then talk again to hear their thanks
  and see the next blueprint appear (still costs research time — this reveals it, it doesn't
  unlock it for free). Chains run in order and never skip ahead.
  - Inn Keeper (8 quests: odd jobs, a hot stew, ale, wine, dried foragables, a fireside story, Ash's
    return, the boar-hunt epilogue) reveals painted dishware, wind chimes, a fire-hardened spear,
    and a wooden shield. Two friendship milestones (15/30) separately reveal a Comfortable Bed and
    Copper Bed Frame.
  - Miller (3 quests: grain, leather for the grindstone belts, planks) reveals the Garden Trellis,
    Hunting Stand, and Stone Tiles; a trust milestone (25) reveals a Bear Figurine.
  - Weaver (7 quests spanning fiber, cloth, and leather) reveals the Straw Mat, Woven Wall Hanging,
    Chaperon, Long Johns, Cloth Coat, Leather Apron, and Leather Sandals; a trust milestone (25)
    reveals the Cloth Scarf blueprint itself.
  - Apothecary (2 quests: fresh herbs, then charcoal and dried flowers for pigment) reveals Herb
    Paste and Pigment. The Stimulant/Anti-Nausea Tea reveals (Herbs & Fungi side) are unchanged.
  - Professor (4 quests: his existing weekly specimen commission, then clay, metal nuggets, and
    bones/antler) reveals a Quilted Cap, Castle Figurine, Dragon Figurine, and Bone Lamellar Armor.
  - Five cross-villager side quests reveal one item each once both participants' prerequisites are
    met (never blocking a main chain): Tricolor Wall Hanging (Weaver + Apothecary), Cloth Mask
    (Weaver + Apothecary), Painted Plate (Miller + Inn Keeper), Quilted Vest (Weaver + Professor),
    Incense Burner (Apothecary + Inn Keeper).
  - Village Renown milestones (25/50) reveal Clay Beads and a Stone Mace.
- New hidden GameStats drive the chain chassis (`cmcStat<NPC>QuestChain/QuestArmed/LastQuestWeek`
  per villager) plus three player-side mirrors of NPC trust values, all ticked by a new
  `QuestChainSchedulePatch` (one new 5s poll covering all five villagers).

### Known issues / needs in-game confirmation
- The Inn Keeper's "bring a hot cooked stew" quest currently accepts any water-filled container
  (`LQ_Water`), not specifically a thickened stew — the game has no Progress/thickness check
  available on a drag-and-drop delivery action, only liquid identity and quantity. Will likely be
  tightened or re-flavored in a follow-up once playtested.
- The ale and wine delivery actions (Inn Keeper quests 3 and 4) accept liquid-type cards
  (`LQ_RyeAle`/`LQ_WheatAle`, three wine varieties) via drag-and-drop — this mod has no prior
  precedent of a liquid card being accepted this way, so it needs an in-game click-test before
  being considered confirmed working.

## [1.25.0] — 2026-07-22

### Added — Village Hall is now enterable, with a Boards room
- The Village Hall now has an **"Enter the Village Hall"** action leading to an interior room,
  temporarily reusing the Village Academy's interior artwork as a placeholder
  (`cmcVillageHallInterior` / `cmcVillageHallInteriorLocation`) until dedicated Town Hall art is
  ready to swap in.
- Inside, six notice boards are on display: a **Town Board** (general village renown/standing,
  using the Village Hall's own image) and one board each for the **Inn Keeper, Miller, Weaver,
  Apothecary, and Professor**, each showing that NPC's own portrait artwork. Each board is a
  simple flavor-text notice (0-cost "Read" action) — no quest-chain gating yet; that lands with
  the planned quest-chain chassis (`Village_Master_Plan.md` §10.6).
- The Village Hall's original outdoor "Read Notice Board" action (tracking the village's overall
  Renown stat) is unchanged and still lives on the Village Hall's own card, representing the
  town's general reputation.

## [1.24.2] — 2026-07-22

### Fixed — 7 truncated / malformed item & NPC descriptions
- Seven `Localization/SimpEn.csv` rows held an unquoted English value with an internal comma,
  which the loader truncates at the first comma (the remainder is misread as the Chinese
  column) — the card/NPC showed only a half-sentence. Down Mattress (item + blueprint), Straw
  Mat (placed), Painted Plate, the Professor's day-gated greeting, and a hidden stat
  description are now fully shown; the Inn Keeper's description also had a stray `, a bed`
  fragment left outside its quotes, now removed. All values are now double-quoted.

## [1.24.1] — 2026-07-22

### Fixed — Village Founder buildings spawned at the wrong location
- The perk's 4 pre-built buildings spawned on whatever board the player stood on when the
  perk applied (the run's STARTING location, e.g. River Clearing) — `SpawnService.Spawn`
  only targets the current board. Placement is now deferred per building to the first time
  the player stands in its home environment: Miller's Cottage, Weaver's Cottage, and the
  Village Hall appear at the **Village** (`cmcEnvVillage`), the Apothecary's Cabin at the
  **Foraging Forest** (`cmcEnvForagingForest`). Latched per env by two new hidden GameStats
  (`cmcStatFounderVillagePlaced`, `cmcStatFounderForestPlaced`) so a deliberately
  deconstructed building is not force-respawned on later visits.
- Saves already affected: the misplaced copies on the starting board are not auto-removed
  (deconstruct them manually, or start a fresh run); correct copies appear at the proper
  locations on next visit.

### Changed — the 4 village building blueprints are now location-locked
- `BpCMCCottageMiller`, `BpCMCCottageWeaver`, `BpCMCVillageHall` can now ONLY be built at
  the Village; `BpCMCApothecaryCabin` can ONLY be built in the Foraging Forest. Enforced via
  `BlueprintCardConditions` (gates the "Start Building" model placement) + matching
  `BuildingCardConditions` (gates every construction stage), both requiring the location's
  CT8 card on the board. Blueprint descriptions now state the build location.

## [1.24.0] — 2026-07-22

### Added — Village Founder perk (cheat/head-start perk)
- New optional character creation perk, **Village Founder** (`cmcperkvillagefounder`, 3 Stars):
  instantly fast-forwards every currently-shipped village beat at game start. Places all 4
  village structures (Miller's Cottage, Weaver's Cottage, Village Hall, Apothecary's Cabin)
  pre-built on their boards; moves the Miller, Weaver, and Apothecary in immediately instead of
  the usual 7-day wait; marks the Inn Keeper/Professor intro done and all 4 single-shot NPC
  quests (Miller Grain, Weaver Flax, Apothecary Herbs, Professor Specimen) thanked, granting
  their blueprint unlocks (`BpCMCGardenTrellis`, Hunting Stand, all 3 comfort blueprints,
  Herb Paste, Market Stall); and grants all 6 Academy graduate perks.
- Does **not** touch anything from the still-unbuilt design layer (Town Hall Boards, Shadow
  the Cat, the 37-item NPC-quest-chain reveal sweep, Trust/Renown-gated item reveals) since
  none of that exists as spawnable content yet — see `Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md`.
- New file: `Patcher/VillageFounderPerkPatch.cs` (gated one-shot apply via
  `cmcStatVillageFounderApplied`, using the same StatsDict/CurrentBaseValue GameStat-write
  idiom as `VillageClock`/`CottageResidentSpawnPatch`, and the InRunAddedPerks-list-append +
  `ApplyPerk` pattern proven in `Sirus23_Mod_Collection/Patcher/CompanionHuntPatch`).

## [1.23.0] — 2026-07-21

### Changed — shared village construction backbone (Cottage Rework §3.7)
- **Miller's Cottage, Weaver's Cottage, and the Village Hall are now built the same way every
  village residence will be**: six shared stages (foundation/walls ×4 — Heavy Stone ×6 + Tree
  Log ×6 each; frame — Tree Log ×8; roof — Plank ×10) plus one themed finishing stage per
  building — Miller: Rope ×8 + Wood ×12; Weaver: Cloth ×14 + Fibers ×10 + Rope ×12; Village
  Hall (civic): Plaster ×20 + Clay ×16 + Heavy Stone ×10. This is the same backbone the
  Apothecary's Cabin already uses (verified in-game 2026-07-21).
- **Balance callout (deliberate, large):** cottage research time rises 12 → 192 ticks (16×) and
  total build time 6–7 → 84 daytime units (~12–14×, 12 per stage across 7 stages); the Village
  Hall's research rises 48 → 192 (4×) and build 36 → 84 (~2.3×), while its per-stage material
  bill drops from the old 30/30/30 walls stage to the shared backbone's steadier pace. These
  are landmark civic projects meant to be paced across village weeks, not an afternoon.
- Weaver's Cottage and Village Hall blueprint descriptions rewritten to match the new
  construction (the Hall's old text described a three-stage build; the Weaver's claimed it
  wasn't stone-built).

### Diagnostics
- **Fixed the 1.22.3 `BlueprintVisibilityDiagnostic` so it actually runs.** It targeted
  `GameManager.LoadMainGameData` — a method that lives on `GameLoad`, and which runs before
  `BlueprintModelStates` exists at all. It now postfixes `GameManager.InitializeStatsAndActions`
  (the method that actually populates the dictionary, at run start), corrects the Garden
  Trellis UID casing (`BpCMCGardenTrellis`), adds the Hunting Stand
  (`hunterscachebphuntingstand`), and logs `BlueprintPurchasing` plus the dictionary size.
  Note: re-analysis of the T1.6 "still visible" report found no runtime evidence the 1.22.2
  hide fix ever failed — the observation was made 8 minutes after the deploy, likely against a
  game process started before it. The decompiled Hidden/Locked branch confirms the shipped
  JSON recipe is correct; the diagnostic stays in until a fresh-run journal check settles it.

## [1.22.4] — 2026-07-21

### Changed
- **Village location cards showed a wall of flavor text that pushed the Trees/Overgrowth/Foraging
  capacity bars and the Forage/Clear buttons off screen.** All twelve village map locations (Village
  Path, Village, Village Farm, Pine Trail, High Grove, Foraging Forest, Deer Meadow, Hunter's
  Crossing, Badger Warren, Clay Flats, Marsh Hollow, Mossy Clearing) carried a 3–5 sentence
  `CardDescription` where vanilla location cards use one short sentence. Trimmed each
  `CardDescription` to a single-sentence blurb matching vanilla scale; the full Conditions/Flora/
  Fauna detail already lived in each location's `CardHelpSection` (the Help tab) and is unchanged.
  Added the missing `CardDescription` rows to `Localization/SimpEn.csv` (previously JSON
  `DefaultText`-only, silently falling back since no CSV row existed).

## [1.22.3] — 2026-07-21

### Diagnostics
- **The 1.22.2 quest-reward blueprint hide fix did not hold up in playtest** — Straw Mat, Woven
  Wall Hanging, Tricolor Wall Hanging, and Garden Trellis were still visible in the crafting
  journal from a fresh run despite the JSON matching the documented `Hidden`-state recipe exactly.
  Added a temporary startup diagnostic (`BlueprintVisibilityDiagnostic.cs`) that logs each item's
  actual resolved `BlueprintModelState` and `UnlockConditionsDesc` contents right after
  `GameManager.LoadMainGameData` — needed to see what the game actually resolved before attempting
  another fix. Remove once root-caused. See `Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md` §3.4
  ("Spike 1 result: FAILED").

### Fixed
- **East travel from River Clearing showed a red X even with the River Bridge built and/or the
  Village Pathfinder perk.** The Village Path connection gate used `"RestoreDAOnUnlock": false`,
  so any run start where the gate evaluated locked (e.g. loading a character without the perk
  first, or perk state not yet readable) stripped the East travel action off River Clearing for
  the whole game process — and unlocking the gate later re-showed the map connection but never
  gave the travel button back. The gate now restores the travel action when it unlocks
  (`"RestoreDAOnUnlock": true`). Confirmed from the live load log: gate locked at run start,
  bridge/perk recognized later in the same process, gate flipped unlocked, button never returned.

## [1.22.2] — 2026-07-21

### Fixed
- **Quest-reward blueprints were visible in the crafting journal before their quest was done.**
  The Weaver's flax-errand rewards (Woven Wall Hanging, Straw Mat, Tricolor Wall Hanging) and the
  Miller's grain-errand rewards (Garden Trellis, Hunting Stand) are meant to be learned only by
  helping those villagers — but four of the five were listed in a crafting-journal tab, and all
  five carried a "Help the Weaver/Miller with their errand" unlock hint, so they showed up as
  greyed, teased entries from the start. They are now fully hidden — no card, no teaser — until
  the errand is completed and thanked, at which point the reward conversation teaches the recipe
  and it appears in its normal tab. (Removed the four tab registrations; blanked the unlock-hint
  text so the blueprints load in the game's `Hidden` state rather than `Locked`. Herb Paste is
  unchanged — it stays a materials teaser, since it's learnable by foraging Wild Garlic + Old
  Growth Bark, not only by the Apothecary quest.)

## [1.22.1] — 2026-07-20

### Fixed
- **Village Inn trait: first-ever conversation was the wild-garlic errand, with no welcome at all.**
  The trait backfills `cmcStatVillagePhase` to 1 in the background within seconds of loading (so
  the intro conversation never plays), but nothing filled that gap — a trait character's first
  `Talk` could land directly on whatever week/errand beat the phase gate had already opened,
  cold. Added a one-time `CMC_InnKeeperTalk_TraitWelcome` line (new `cmcStatInnTraitWelcomeGiven`
  latch) that now plays first for exactly that path: acknowledges the head start, gives the same
  "inn/projects/Academy are open" recap the normal intro conversation ends on. Players who take
  the normal intro conversation, or whose save is backfilled from pre-rework village progress,
  never see it (the latch is set for them too, since they already got an equivalent welcome).

## [1.22.0] — 2026-07-20

### Added — Village Rework PR-1: phase gate, Village Clock, intro conversation
- **The village now opens in phases, at the player's pace.** A fresh character sees a quiet
  village: the Inn Keeper offers only a new 4-line **introduction conversation** (the valley's
  money system — salt/nuggets/coins, the Inn account, Academy tuition — and the three construction
  projects the village dreams of), the Professor redirects to the Inn ("introductions first"),
  neither NPC trades, Academy study is politely refused with a tooltip, and the Miller's Cottage /
  Weaver's Cottage / Village Hall blueprints stay out of the crafting journal. Finishing the intro
  sets the new hidden `cmcStatVillagePhase` to 1 and opens all of it at once (blueprints still
  require research). A "Remind me how the village works" answer on his normal greeting replays a
  condensed recap any time.
- **Village Clock** (`Patcher/VillageClock.cs`, ticked inside the existing 5 s Inn Keeper poll —
  no new polls): `cmcStatVillageEpochDay` is stamped once when phase 1 is first observed, and
  `cmcStatVillageWeek` is recomputed from it every poll (derived, reload-safe). **No Inn Keeper
  beat reads the absolute world calendar anymore**: the settled-in line paces to village week 2,
  the backstory to week 3, the odd-jobs errand to week 2, the seasonal fireside story pool to
  week 4, and Ash's disappearance arms no earlier than week 6 (still requiring the Well). A new
  `cmcStatInnOnboardStep` sequencer delivers at most one onboarding beat per conversation, so a
  late-arriving player binges them one visit at a time instead of collapsing three weeks of
  dialog into one greeting.
- **Save back-compat:** pre-rework saves with real village progress (the Well Plans, or any
  completed villager quest) are detected within seconds of loading and skip the intro
  automatically; a save that already has the Well Plans also skips both onboarding beats.
  Inn friendship alone deliberately does NOT waive the intro — the Chat action stays available
  pre-intro and builds friendship, so counting it would let one Chat skip the whole onboarding;
  a lightly-engaged old save simply gets the short intro conversation once.

### Changed
- **The Village Inn and Village Academy are now permanent village fixtures** — their map spawns no
  longer require character traits (`ConditionalDrops` re-keyed from `HasPerk` to `AlwaysTrue`).
- **Village Inn trait repurposed** — now a head start instead of a gate: +5 starting Inn
  friendship, and the Inn Keeper waives the formal introductions (the village timeline starts
  open for that character).
- **Higher Education trait retired from character creation** (`CharacterPerkPerkGroup: "None"`;
  the asset ships so existing saves stay valid). Academy course gating now applies to **every**
  character: the Water-Driven Sawmill/Grinding Mill/Forge/Workshop, Copper Sheet, Iron Fishing
  Rod, and copper/iron armor blueprints are locked until the matching course is graduated,
  trait or no trait.
- **Cottage/Hall blueprints re-gated** from "Visit the Village" (`CardsOnBoard: cmcLocVillage`)
  to the intro (`StatValues: cmcStatVillagePhase >= 1`), with unlock descriptions rewritten to
  "Hear the Inn Keeper's plans for the village".

## [1.21.0] — 2026-07-19

### Added (backfilled entry — released as "Bug fixes")
- **The Miller and Weaver now work their cottages.** Miller: mill 3 logs into 24 Planks or grind a
  30-count sack of wheat/rye/edible acorns into flour, each for a copper nugget, via
  station-contained operation blueprints; trades planks and flour, restocking weekly. Weaver:
  weave rope or large cloth at a fiber discount, process dried nettle/flax (and hemp with H&F)
  into fiber batches, trades weaving tools and cordage, also restocking weekly
  (`CottageResidentSpawnPatch` restock chassis + operation blueprints on both cottages).

## [1.20.0] — 2026-07-17

### Added
- **Ash's "Boar Hunt"** — five days after taming Ash the Cat, he slips out after dusk to track a
  boar through the underbrush and doesn't come home (`cmcInnCat` transforms in place into
  `cmcAshTrackingBoar`, art `CMC_InnCat_Wary`, via a new hidden `SpecialDurability2` timer — same
  idiom as his existing Hunger/Thirst "Wanders Off"). He's away — no Feed/Water/Pet/Play — until
  the player wins any real vanilla wild boar encounter (`Combat_EncounterBoar`), at which point a
  new Harmony patch (`Patcher/AshBoarHuntPatch.cs`, postfixing `EncounterPopup.ApplyEncounterResult`)
  transforms him back to `cmcInnCat` with Hunger/Thirst restored to full. The hunt recurs roughly
  every five days after each return. Resolution isn't tied to a specific boar instance — any
  vanilla boar-encounter win while Ash is away resolves it, since no vanilla data links a specific
  wildlife encounter back to a companion card; this keeps the fight itself 100% vanilla rather than
  spawning/tracking a custom NPCAgent boar.

---

## [1.19.1] — 2026-07-17

### Fixed
- **All twelve village-area map locations now show accurate flora/fauna in their in-game Help
  popup**, instead of the vanilla clone template's original text. `CardCloneService.CloneCard`
  only ever refreshes a clone's `CardName` — `CardHelpSection` is left untouched, so every
  `WorldMap/MapNodes.json` clone node was silently displaying its unrelated vanilla template's
  help text (e.g. Village Farm showing its parent template's blank-fauna description instead of
  the seasonal crop fields actually seeded there). Added twelve `GameSourceModify/<LocationUID>.json`
  patches (framework's `GameSourceModifier` phase runs after `WorldMapInjector.PrepareAll` clones
  and registers the location cards, so these resolve cleanly) rewriting `CardHelpSection` for
  Village Path, High Grove, Pine Trail, Village, Village Farm, Clay Flats, Marsh Hollow, Mossy
  Clearing, Foraging Forest, Hunter's Crossing, Deer Meadow, and Badger Warren — each reflecting
  that tile's real vanilla-template flora/fauna plus any mod-specific additions (seasonal fields
  and wild garlic at Village Farm, the Clay Barrow at Clay Flats, the forage bonanza and Ash's
  dusk prowl at Foraging Forest, badger setts at Hunter's Crossing/Badger Warren).

---

## [1.19.0] — 2026-07-17

### Added
- **Three new hunting-terrain map locations**, wired via `WorldMap/MapNodes.json`:
  - **Hunter's Crossing** — fills the gap between Village Path and Pine Trail with a direct
    shortcut (clone of vanilla Badger Hill); badgers den here.
  - **Deer Meadow** — extends the map north past the Village (clone of vanilla Deer Grove); deer
    frequent these woods.
  - **Badger Warren** — the northernmost stop past Deer Meadow (clone of vanilla Badger Hill).
- **Secondary one-way connection to the vanilla Greenfalls location**, opened from Badger Warren
  via `VanillaExits` — a forward-only shortcut out of the village area (no return route back into
  the village from Greenfalls).

---

## [1.18.0] — 2026-07-17

### Added
- **Comfortable Bed** — a new top-tier sleeping structure assembled from two separately
  craftable components: a **Down Mattress** (Feathers ×100, Fibers ×50, Large Cloth ×4, Twine
  ×20, needle) and a **Copper Bed Frame** (Copper Sheet ×3, Wood ×6, Plank ×2, hammering tool —
  requires Advanced Copper Tools). A third blueprint assembles both components into the placed
  Comfortable Bed, which has its own Nap/Sleep actions (stronger rest recovery than the Bedroll)
  and a passive perceived-temperature bonus. A Take Apart action splits the bed back into its
  Down Mattress and Copper Bed Frame so it can be moved to a new camp without re-spending raw
  materials.

---

## [1.17.4] — 2026-07-17

### Fixed
- **Professor trade is now always available**, matching the Inn Keeper. Trade was previously
  gated behind his "Resident" phase (satchel filled to 10 foraged items) — which he could never
  reach while his forage was landing on the ground — so the Trade button never appeared. Removed
  the phase gate and gave him a starting stock of six foraged specimens so there is something to
  buy from the first meeting.
- **Foraged items now go into the Professor's satchel instead of dropping on the ground.** His
  forage and specialty-stock actions now fire from the mod (the same `PerformAction` path the Inn
  Keeper's restock uses, the only one proven to route `DropCardsInsideInventory` into an NPC's own
  inventory) rather than through the native NPC action loop, which was depositing them on the
  environment board.
- **Portrait now matches where you meet him.** The scheduler no longer moves him — or flips his
  card art — while you are in the same location, so he no longer wanders off mid-conversation or
  shows his outdoor portrait while standing indoors, and the dialog/trade portrait resolves
  correctly when you talk to him.

---

## [1.17.3] — 2026-07-17

### Changed
- **World map district re-laid out** — Village and High Grove swapped cells: the Village now
  sits at the northern head of the east spine (20,20) with High Grove taking the middle spot
  (20,0) directly east of Village Path. Village Farm moved to (30,20), directly east of the
  Village. Resulting spine: Village Path → High Grove → Pine Trail → Village → Village Farm.
  The southern wetlands (Clay Flats, Marsh Hollow, Mossy Clearing, Foraging Forest) reconnect
  to the spine through Clay Flats↔Village Path and Mossy Clearing↔High Grove, forming a loop so
  no wing dead-ends. All 11 edges validated: unique cells, cardinal adjacency, travel directions
  match geometry, no collisions with vanilla/ACT/H&F.
- **Clay Flats now seeds the vanilla Clay Barrow** instead of the custom `cmcClayMound`. The
  bespoke CT2 clay-mound card, its placeholder art, and its localization rows were retired — the
  vanilla Clay Barrow's own "Dig Up Clay" interaction covers the same purpose.

---

## [1.17.2] — 2026-07-16

### Fixed
- **World map rearranged into a coherent village district** — the previous layout stretched the
  village into a single column running north along the river (parallel to River Confluence and
  the Metal Mine caves), and **Village Path was permanently hidden from the paper map**
  (`HideFromMap: true`), so the bridge crossing from River Clearing and every path into the
  village cluster was never drawn — the village nodes floated disconnected next to the river.
  Village Path is now visible (still fog-hidden until first visited, like any location) and the
  nine locations form a compact 2×5 block east of the river: wetlands south (Clay Flats, Marsh
  Hollow), village core center (Path, Village, Farm), pine highlands north (Pine Trail, High
  Grove beside the mine caves). Two loop connections added (Farm↔Pine Trail,
  Marsh Hollow↔Foraging Forest) so neither wing dead-ends through the Village Path hub.
- **Village Well was unbuildable** — `InjectImprovementInto.json` targeted the CT4 environment
  UID (`cmcEnvVillage`) instead of the CT8 location card (`cmcLocVillage`), so the injection was
  skipped at every load ("not found in registry" warning). Also requires framework **2.16.3**,
  which moves improvement injection after map-node clone creation so mod map locations can be
  targeted at all.

---

## [1.17.0] — 2026-07-16

### Added

#### Village Professor — location-matched portraits
The Professor's card art and dialog/trading portrait now track where he actually is:
- **Village Academy** → `Professor_Indoors` (study interior).
- **Outdoor forage nodes** (Village Path / Village / Farm / Foraging Forest) → `Professor_Outdoors`.
- **Inn (at night)** → `Professor_Inn` — this PNG does not exist yet; until it ships, the
  Academy art is used at the Inn (one Info log line notes the fallback). Dropping a
  `Professor_Inn.png` into `Resource/Picture/` is the only step needed to activate it.

Implementation: the P2 scheduler (`ProfessorSchedulePatch.SyncPortrait`) swaps
`NPCAgent.AgentImage` (dialog + trade popups read it live), restamps both NPC model cards
(their image is copied from `AgentImage` once at creation), and refreshes any on-board card
graphics so the art flips the same tick he moves. Requires framework **2.16.0**
(`GameContent.Find<Sprite>`).

### Fixed
- **`Agent_Professor.json` pointed at a sprite name that no longer exists** — the P1
  placeholder `Professor.png` was replaced by the `Professor_Indoors`/`Professor_Outdoors`
  art in an earlier commit, but `AgentImageWarpData` still said `"Professor"` (unresolvable →
  blank portrait). Now defaults to `Professor_Indoors`.
- **`SyncNarrativeStats` was never called** — 1.15.0's changelog shipped it (grad-count
  mirror, River-Bridge news flag, weekly specimen-commission re-arm) and the method body was
  present, but the scheduler call site was still the pre-1.15.0 `// TODO` comment, so none of
  that state ever updated. The call is now wired in.

## [1.16.0] — 2026-07-16

### Added — deferred-items revival batch (recovered from the deleted 2026-05-20 standalone mods)
- **Club** (Hunting › Close Combat): a budget blunt weapon carved from a tree trunk — 20–80 blunt damage, 60 durability, carves down into a long stick.
- **Bone Helmet** (Tailoring › Equipment): 18 head armor from 2 Bones + small leather + sinew; armor scales with durability (50%–120%); dismantles back into its materials.
- **Cloth Mask** (Tailoring › Cloth): mask-slot face covering (+0.5 perceived temperature); wears out slowly while worn; rips back into cloth.
- **Long Johns** (Tailoring › Cloth): underwear-slot cloth leggings (+1.5 perceived temperature, +4 comfort, 2/2 leg armor); rips back into 2 cloth.
- **Glazed Clay Plate** (Metal & Clay › Utensils): refire a clay plate with ash glaze — 600 durability tableware (vs 500 painted), stacks to 3.
- **Hunting Stand** (Hunting › Trapping): build a kit (3 Planks + 4 Long Sticks + 2 Rope), set it up at a location, and wild-animal encounters there have a **~45% chance to be averted** (declarative framework `EncounterGuards` JSON — no C#). Take Down returns the kit; Dismantle returns planks and sticks.

### Not revived (with reasons)
- **Sling + Sling Stones** — vanilla EA 0.65 has no moddable ranged-ammunition combat path; shipping a non-functional weapon would be advertised dead content.
- **Grinding Slab, Stone Block, Stone Tile (singular)** — no consumer exists in CMC yet (stone construction blueprints / grinding CI design pending).

## [1.15.0] — 2026-07-16

### Added

#### Village Professor — Trade, Trust & Errands (enhancement pack)
Builds Phase P3 (Trade) plus a dialog/reward layer on the P2 Foraging Arc scheduler
(`Documentation/Design/Village_Professor_Plan.md`). All state lives in the Professor's own
NPCStats (native save path); dialog reads them via explicit-agent `RequiredNPCStatValues`
(decomp-verified: `UseAssociatedAgent` cannot work in dialog conditions — they evaluate with a
null card — but an explicit `TargetAgent` resolves through `GameManager.AllNPCs`).

- **Trade enabled** (`CannotTrade: false`), gated to his Resident phase via `TradingConditions`.
  Baseline barter: sells at 1.5×, buys at 0.5×.
- **Trust** (`cmcStatProfessorTrust`, 0–100) — vanilla `AgentTrust` idiom: every trade feeds it
  via `ModifyNPCStatsPerTradeValue` (both directions, clamped +6/side/trade); the quiz, gifts,
  and commission completions add more.
- **Price break at high trust** — trust-scaled `NPCStatModifiers` on both trade directions:
  at trust 100 he sells at 1.1× and pays 0.75× (linear from 1.5×/0.5× at trust 0).
- **Trust-tiered greetings** (≥25 friendly, ≥60 personal) plus two trust-gated branches:
  field-scholar advice (≥25) and a two-part backstory (≥60).
- **Seasonal greetings** — four season-gated starting lines (`InGameTimeCondition.SeasonIs`).
- **"Where were you today?"** — the P2 scheduler now records each wander destination in
  `cmcStatProfLastNode`; a menu answer surfaces one of four node-flavored responses.
- **Milestone-reactive dialog** — scheduler mirrors graduated-course count into
  `cmcStatProfGradCount` (studies check-in at 0 / 1–5 / 6) and flags the River Bridge via
  `CardUtil.IsImprovementBuilt` (`cmcStatProfBridgeNews`, one-time news line); a one-time
  Ash-the-cat line gates on the Inn Keeper's `cmcStatLostCat` quest stat.
- **Milestone gift ladder** — one-time gifts as self-disarming priority greetings (CatThanks
  pattern): first degree = 2× Metal Nugget (+5 trust); all six degrees ("Valedictorian") =
  4× Metal Nugget + 6× Salt (+10 trust).
- **Entrance-exam quiz** — three-choice question; the correct answer pays 2× Salt (+5 trust),
  once; wrong answers allow retakes.
- **Weekly specimen commission** (`bpcmcprofspecimen`, first commission on `CommissionsBp`) —
  bring 3 Nettle Leaves for 1 Metal Nugget (+8 trust); completion disarms it and the scheduler
  re-arms it 7 days later (`cmcStatProfSpecimenReady`/`cmcStatProfSpecimenNextDay`). Commission
  blueprints are deliberately NOT in `BlueprintTabs.json`.
- 10 new `NPCStat/*.json`, 25 new/updated `DialogScene`/`DialogLine` assets, 41 new
  localization rows; scheduler gains `SyncNarrativeStats` (grad count, bridge news, weekly
  re-arm) with once-true caches reset per run.

#### Cottage Residents — the Miller & the Weaver move in
Resident phase of `Documentation/Design/Village_Construction_Projects_Plan.md`: each cottage
gains a resident NPC who **moves in one week (7 in-game days) after the cottage is finished**.
No interiors — the resident stands on the Village board beside their home.

- **The Miller** (`cmcMillerAgent`) and **the Weaver** (`cmcWeaverAgent`) — stationary NPCAgents
  on the Inn Keeper/Professor chassis: a Talk dialog (`CMC_MillerTalk` / `CMC_WeaverTalk`, three
  lines each — greeting, craft talk, village talk) plus a Chat `DismantleAction` with the same
  social payoff as the Inn Keeper's Chat (Loneliness/Stress relief, Comfort, 1-hour cost).
  No trade (`CannotTrade: true`) — deliberately smaller in scope than the Inn Keeper.
- **Move-in timer** — new hidden GameStats `cmcStatMillerMoveIn` / `cmcStatWeaverMoveIn` store
  the absolute arrival day. `CottageResidentSpawnPatch` arms each to (completion day + 7) the
  first tick the built cottage exists (a `GameManager.AllCards` scan — note `AllCards` is
  current-environment-scoped, so this fires while the player stands at the Village, which in
  practice is completion time since construction happens on that board), then spawns the
  resident the first time the player stands at the Village on/after the due day
  (`CreateNPC` + `Init(live EnvID)` + `AssignOrCreateNPCCards`), with an
  `InitializeStatsAndActions` postfix restoring placed residents on save load — a structural
  copy of `InnKeeperSpawnPatch` minus the restock machinery. GameStats persist natively, so
  the countdown survives save/reload.
- Ships with solid-white placeholder portraits (`CMC_Miller.png`, `CMC_Weaver.png`) pending
  real art, matching the cottages' own placeholder card art.

### Requires
- CSFF Mod Framework **2.15.1+** (non-UID SO body WarpData resolution — dialog assets).

---

## [1.14.0] — 2026-07-16

### Added

#### Village Construction Projects — Miller's Cottage, Weaver's Cottage, Village Hall
Phase 1 of `Documentation/Design/Village_Construction_Projects_Plan.md` — three buildable civic
structures at the Village node, no interiors, no new NPCs (deliberately deferred to a future Phase 2).

- **Miller's Cottage** (`cmcCottageMiller` CT2 / `BpCMCCottageMiller` CT7) — single-stage build
  (Stone ×30, Plank ×16, Rope ×8, Wood ×12), gated on having visited the Village. Comfort +14 passive.
  No `DismantleActions` — permanent civic growth, matching the Well/Bridge precedent, not the packable
  Market Stall kit.
- **Weaver's Cottage** (`cmcCottageWeaver` CT2 / `BpCMCCottageWeaver` CT7) — same shape, cloth/rope
  material flavor instead of stone (Cloth ×14, Rope ×12, Plank ×14, Fibers ×10). Comfort +10 passive —
  deliberately different from Miller's so the two don't feel like reskins.
- **Village Hall** (`cmcVillageHall` CT2 / `BpCMCVillageHall` CT7) — the capstone, a 3-stage build
  (foundation: Stone/Wood/Twine; walls: Heavy Stone/Clay/Plaster; roof: Plank/Rope — ~235 items total,
  ~1.75× the Well's build cost) each stage at `BuildingDaytimeCost: 12`, the framework's per-stage cap.
- **Village Renown** (`cmcStatVillageRenown`, hidden `GameStat`, Visibility 2) — new `VillageRenownPatch`
  live-recomputes each tick from which civic structures currently exist (both cottages, the Well, the
  River Bridge, and a Market Stall revenue milestone), rather than incrementing once per completion.
  Recomputing avoids a real multi-stage pitfall: `CardData.BlueprintStatModifications` feeds
  `CurrentBuildAction.StatModifications` for *every* stage of a blueprint's build action
  (`BlueprintConstructionPopup`), so a naive "tick once on completion" via that field would have fired
  three times on Village Hall alone.
- Village Hall's Notice Board mirrors current Renown onto its own `SpecialDurability1` bar (a visible
  progress readout) and a flavor "Read Notice Board" `DismantleAction`. At full Renown, Market Stall
  sales get a 25% revenue bonus (`MarketStallPatch.RevenueMultiplier`) — a real, working payoff rather
  than the speculative `RatePerDaytimePoint` nudge floated in the design doc (Market Stall's revenue is
  event-driven via `MarketStallPatch.OnDayChanged`, not rate-based, so that field is inert for it).
- Registered in `BlueprintTabs.json` under the vanilla `Tab_2_Construction_Subtab_5_HouseBuilding_TabName`
  tab (verified against its actual `TabName.LocalizationKey`, not the tab file's name).
- Ships with solid-white placeholder card art (`CMC_CottageMiller.png`, `CMC_CottageWeaver.png`,
  `CMC_VillageHall.png`) pending real art.
- Phase 2 (interiors + resident NPCs for any of the three) is explicitly out of scope for this release —
  see the plan doc §1 and §8 for the sequencing/risk rationale.

#### Inn Keeper — Friendship, seasonal wares, a gift, an errand, and village-progress banter
Phase 7 of `Documentation/Design/Village_InnKeeper_Plan.md` — deepens the existing Inn Keeper NPC
without new subsystems; reuses proven hidden-`GameStat` state-machine and `DragAndDropActions`
patterns throughout.

- **Friendship** (`cmcStatInnFriendship`, hidden, 0-30): rises with Chat/Gift/errand delivery.
  At 15+, unlocks a warmer greeting variant and the one-time **Inn Regular** perk
  (`Pk_InnRegular.json`, small passive Loneliness/Stress rate reduction, granted via a new "Feel at
  Home" `DismantleAction`). Also feeds a Trade discount via `NPCTradingValueModifier.
  GlobalStatModifiers` on the existing markup/buyback modifiers — markup falls 1.5x -> 1.2x, buyback
  rises 0.5x -> 0.65x as Friendship approaches 20.
- **Progress-reactive dialog**: one-shot remarks when the Village Well or River Bridge is finished
  (`CardsOnBoard`-gated `StartingPoint` lines), followed by a one-shot Well "housewarming" dialog beat
  (an invite + toast, narrated only — see below for why an actual NPC departure wasn't built).
- **Seasonal pantry**: four new seasonal restock `AgentActions` swap in season-flavored stock (fresh
  Billberries in summer, Roasted Acorns in autumn, Fermented Billberries + Dried Bird Meat in winter,
  etc.) alongside the base pantry.
- **Gift**: drag foraged Wild Garlic onto the Keeper (new `DragAndDropActions` entry) for a one-time
  `BlueprintsFullUnlock` of the Market Stall blueprint.
- **Odd-jobs errand**: an occasional "bring me Dried Wild Garlic" request (from day 8), delivered via
  drag-and-drop for a small reward; capped at once per season.
- **Cross-NPC banter**: new dialog answers let you ask the Inn Keeper about the Professor and vice
  versa.
- **Scope note**: a genuine "Keeper leaves the Inn for the Well celebration" mechanic (physical
  `MoveToEnvironment`) was investigated and NOT built — `NPCAction.ToAction()` converts to a plain
  `CardAction` that drops movement data entirely, so this mod's existing direct-invoke restock/errand
  pattern (chosen specifically to bypass native NPC action scheduling) cannot move the Keeper; doing so
  safely would mean re-enabling native scheduling for one action, risking the documented
  sibling-action race. The narrative payoff ships via dialog only; physical departure remains a future,
  separately-scoped effort. "Ash living at the Inn" (Stay Here/Follow Me) was also investigated and
  dropped as unnecessary — Ash has no `AlwaysUpdate` flag (matching vanilla `DogFriend`), so leaving him
  behind or bringing him along via ordinary inventory drag already works with zero new code.

---

## [1.13.0] — 2026-07-16

### Added

#### Village map expansion — north/south trails, relocated Foraging Forest, Clay Mound
- **Northern trail** — three new `WorldMap/MapNodes.json` clone nodes beyond the Village (not Village
  Farm — see collision note below): **Mossy Clearing** → **Pine Trail** → **High Grove**, at
  `(20,0,10)`/`(20,0,20)`/`(20,0,30)`, 10 units apart. Pure JSON, no C# — each node clones a distinct
  vanilla outdoor template (`Env_ClearingOak_MossyClearing`, `Env_GrovePine_PineGrove`,
  `Env_GrovePine_HighGrove`) so it arrives with correct vanilla biome tags, ambience, and forage
  tables out of the box.
- **Coordinate collision caught in review** — the northern trail was originally planned at
  `x:10,z:20/30/40` (continuing straight out from Village Farm), which turned out to exactly collide
  with `AdvancedCopperTools/WorldMap/MapNodes.json`'s `actIronCaveEnv`/`actTinCaveEnv`/`actCopperCaveEnv`
  (same three cells). An independent QA pass caught this — the original collision check only compared
  against vanilla `DefaultWorldMap.json`, not sibling mods' own `MapNodes.json`. Moved to Village's own
  `x:20` column instead, confirmed clear against vanilla, ACT (`x:10` only), and HerbsAndFungi
  (`x:-70/-80` only). Lesson for future map-expansion work in this repo: always check ALL mods'
  `WorldMap/MapNodes.json`/`FullMap.json` files for the target cells, not just vanilla.
- **Southern trail** — two new nodes between Village Path and the Foraging Forest: **Clay Flats**
  (cloned from vanilla `Env_River_ClearingAlder_BoggyMeadows`, a riverside marsh) and **Marsh Hollow**
  (cloned from vanilla `Env_ClearingAlder_SwampHill`).
- **Foraging Forest relocated** — moved two steps further out along the southern trail (past Clay
  Flats and Marsh Hollow) and re-templated from the vanilla Flower Glade to the vanilla **Deer Grove**
  (`Env_GrovePine_DeerGrove`) so deer are thematically at home in these woods. `EnvironmentUID`/
  `LocationUID` are unchanged, so the Ash lost-cat prowl gate (`LostCatPatch.TargetEnvUid`), the
  Professor's outdoor schedule (`ProfessorSchedulePatch.OutdoorNodeUids`), and the Wild
  Garlic/Old Growth Bark/H&F forage drop table (`DropInjections.json`, keyed on
  `cmcLocForagingForest`) all continue to work unchanged — none of them key off coordinates or the
  clone template. Existing saves that already visited the old Foraging Forest keep whatever board
  content they already generated there; only new/not-yet-visited games see the Deer Grove reseed.
- **Clay Mound** (`cmcClayMound`, new `CardData/Location/`) — a `CardType 2` natural clay deposit
  seeded onto Clay Flats via `ExtraDropUIDs`. Two `CardInteractions`, mirroring Advanced Copper
  Tools' ore-vein pattern: **Dig Clay** (Shovel only; 2 Clay guaranteed + 40% chance of a 3rd) and
  **Scoop Clay** (any other digging/chopping tool; 1 Clay, slower). 12 `UsageDurability` charges
  total before the mound is spent (`ReceivingCardChanges.ModType: 3` on zero, matching
  `AdvancedCopperTools/CardData/Location/CopperVein.json`). Ships with a solid-white placeholder
  PNG pending real art (prompt drafted in `.audit/image-prompt-claymound.md`).
- Village area location count: 4 → 9. `ModInfo.json`/`README.md` updated for feature honesty.

---

## [1.12.0] — 2026-07-16

### Added

#### Village Inn Keeper — weekly dialog, the Village Well, and Ash's return
- **Weekly onboarding arc** — the Keeper's greeting now changes over the first three weeks: week 1
  greets the player and offers a foraging tip or points them to a bed/odd jobs; week 2 nods to the
  turning season and opens Trade directly from the greeting; week 3 shares his backstory (arriving
  in the valley as a boy, his father digging the cellar, the homestead growing into the Inn) and
  offers **Well Plans** if you agree to help the village grow. All new lines are additional
  `StartingPoint` `DialogLine` entries gated by `RequiredInGameTimes` (`DayIs >= 8/21`) — no C#
  dialog-routing needed, the engine's own `DialogScene.GetStartingLine` gate list handles it.
- **The Village Well** — a new buildable `CardType 10` improvement (`cmcimpwell`), unlocked by the
  Well Plans item from the week-3 conversation. Twice the Stone/Heavy Stone cost of the vanilla
  root cellar, plus Plaster/Clay/Twine, at the maximum single-stage construction time (12). Once
  built, drag any empty water container to it to fill (`CreatedLiquidInGivenCard`, same idiom as
  vanilla ponds/rivers). Injected into the Village map via `InjectImprovementInto.json`.
- **A seasonal story pool** — five independent folk tales (a hard winter with wolves, a
  mushroom-addled night, a tall fish tale, a miller's wager, and a ferryman's uncanny crossing),
  each told across 4 visits over the span of a season. One story is randomly selected whenever the
  season changes (`Patcher/InnKeeperDialogSchedulePatch.cs`); progress within a story advances via
  the same self-advancing `StatModifications` idiom as the lost-cat arc.
- **Ash's disappearance, take two** — starting day 42, once the Well is built, the Keeper's very
  next greeting becomes a special reveal about Ash going missing (replacing whatever week/story
  greeting would otherwise show), arming the existing lost-cat search. Once Ash is found and
  thanked, the Keeper occasionally checks in asking how Ash is settling in, with several possible
  replies.

### Fixed

#### Village Inn Keeper — empty trade pantry on first meeting
- The Keeper's pantry could show empty if Trade was opened within the first ~30 seconds of
  meeting him — `StartingInventory`'s `ItemWarpData` resolution path is unproven for
  `NPCInventoryElement`, and the periodic restock (the confirmed-working mechanism) only ran on
  its own 30-second poll. The pantry is now force-restocked immediately the moment the Keeper's
  board card is created (arrival spawn or save-reload restore), reusing the same proven
  `DropCardsInsideInventory` restock action rather than waiting on the poll.

### Changed

- Em dashes removed from all Inn Keeper dialog and related card text (Ash the Cat, Ash the Stray
  Cat, the Keeper's own description) per a wording pass — replaced with commas/colons.

---

## [1.11.5] — 2026-07-15

### Added

#### Village Professor — Foraging Arc (Phase P2)
- The Professor now has a daily routine instead of standing still in the Academy: by day he
  wanders the four Village-area map nodes (Village Path, Village Farm, Village, Foraging Forest)
  foraging small amounts of vanilla goods into his own satchel; every night, regardless of phase,
  he returns to the Inn. Once his satchel holds 10 items he settles in at the Academy by day
  instead — and heads back out foraging again if his stock drops to 2 or fewer (repeating cycle).
- **Specialty stock** — once a course is graduated at the Academy lectern, the Professor
  occasionally turns up one themed advanced item (a copper tea kettle for Metallurgy, a rotary
  quern for Herbalism, and similarly for Medicine/Fishing/Architecture/Armorer) alongside his
  foraged goods, up to 2 held at a time. Items from mods you don't have installed simply never
  appear — no error, no missing-dependency warning.
- All new movement and state (`Patcher/ProfessorSchedulePatch.cs`, renamed from
  `ProfessorSpawnPatch.cs`) is driven by `GameManager.MoveNPC`; new-item creation is delegated
  entirely to native `NPCAction.DroppedCards` JSON on `Agent_Professor.json`, gated by seven new
  hidden `NPCStat`s (`NPCStat/CMC_Prof*.json`) — no reflection into the game's card-spawn
  coroutine. Trade (exposing the accumulated stock to the player) is Phase P3.

---

## [1.11.4] — 2026-07-15

### Added

#### Village Inn Keeper — Trade, fireside tales, and the lost cat
- **Trade** — the Inn Keeper now buys and sells (sells at 1.5× value, pays 0.5×). The pantry stocks Salt, Acorn Flatbread, Cooked/Dried Meat, Boiled Eggs, a Firm Cheese Wedge, Dried Springberries, and dried spices (Wild Garlic, Garlic Powder, Forest Caps), and restocks a few staples every morning (6–8 AM).
- **Talk** — a new fireside dialog (`CMC_InnKeeperTalk`): the Keeper tells a tale of the forest's real places and legends (River Clearing, Green Glade, the Old Woods and Primeval Woods, Moon Hills, the Black Mire; the Wandering Oak, the Hag, the Humming Widow, the Primeval Wolf). A "Let's see your pantry" answer opens Trade directly.
- **Ash, the lost cat** — the tale turns to the inn's missing mouser. Agree to look for him and Ash begins prowling the **Foraging Forest between dusk and dawn (17:00–06:00)**. Offer him meat or fish and he becomes **Ash the Cat**, a keepable pet with Hunger/Thirst (Feed / Give Water), Pet and Play actions, and a passive Loneliness-reducing aura — but starve him and he wanders off. Report back to the Inn Keeper for a thank-you meal and salt. Quest state is tracked in a hidden GameStat (`cmcStatLostCat`); the spawn check is `Patcher/LostCatPatch.cs`.

### Changed
- Inn Counter description now refers to the visible Inn Keeper.
- Framework requirement raised to **2.13.0+** (non-UID ScriptableObject registration used by the dialog scenes).

---

## [1.11.3] — 2026-07-14

### Added

#### Village Professor
- **Professor** — a stationary NPC now spawns inside the Village Academy on first visit and persists across save/reload. Built as a raw `NPCAgent` (`Agent_Professor.json`); spawn placement/restoration handled by `ProfessorSpawnPatch.cs` (same proven pattern as the Inn Keeper).
- **Talk** — opens a greeting dialog scene with flavor about the Academy and a branch that only appears after day 3, proving the day-gated trigger mechanism. Trade, tasks, and milestone gifts are planned for later phases — see `Documentation/Design/Village_Professor_Plan.md`.

---

## [1.11.2] — 2026-07-14

### Added

#### Village Inn Keeper
- **Inn Keeper** — a stationary NPC now spawns inside the Village Inn on first visit and persists across save/reload. Built as a raw `NPCAgent` (`Agent_InnKeeper.json`); spawn placement/restoration handled by `InnKeeperSpawnPatch.cs`.
- **Chat** — talk with the Inn Keeper for a small boost to Loneliness, Wellbeing, Isolation, Comfort, Morale, Skill_Socials, and Stress (costs 1 in-game hour). Trade and further polish are planned for later phases — see `Documentation/Design/Village_InnKeeper_Plan.md`.

---

## [1.11.1] — 2026-07-14

### Changed

#### Village Academy
- **Armorer course** now also gates **Advanced Copper Tools' Iron armor** (Breastplate, Helmet, Greaves, Gauntlets), matching the existing Copper armor gating — both metal tiers stay locked until the Armorer course's Final Exam is passed. Perk and course descriptions (`Higher Education`, `Armorer Graduate`, lectern Final Exam text) updated to mention iron armor alongside copper.

---

## [1.11.0] — 2026-07-14

### Added

#### Stone Tile Floor
- **Stone Tile Floor** — new environment improvement buildable inside cabins and mud huts (main rooms, expansions, and the free-build construction rooms — 7 interiors total). Lay **Stone Tiles ×12 + Clay ×4** (2-hour build) for a permanent **Comfort +12** and **Insulation** (+1 perceived temperature) in that room. Appears in a room's improvements panel once you have at least one Stone Tile.
- **Stone Tiles** now have a real downstream use: item and blueprint descriptions updated from "trade good only" to reflect their flooring role (closes the `STONE_TILES_FLOORING` deferred idea).

---

## [1.10.3] — 2026-07-12

### Added

#### Village Academy
- **Village Academy** — unlock with the **Higher Education** character trait. Enter from the Village like a vanilla cabin. Drop Salt, metal Nuggets, or a Duros Coinage Metal Coin onto the Lectern to fund a **Tuition Account** (holds up to 1,000). Six courses run as 2-hour study sessions, each with a one-time 100-currency enrollment fee drawn on the first session:
  - **Architecture** (72 h total) — unlocks the Water-Driven Sawmill and Grinding Mill blueprints; together with Metallurgy also unlocks the Water-Driven Forge and Workshop Kit
  - **Metallurgy** (60 h) — unlocks the Copper Sheet recipe
  - **Herbalism** (30 h) — doubles forage yields at all forage locations
  - **Fishing** (24 h) — unlocks the Iron Fishing Rod recipe; improves Iron Rod catch odds
  - **Armorer** (24 h) — unlocks copper armor recipes (course hidden if Advanced Copper Tools is not installed)
  - **Medicine** (24 h) — speeds wound clotting and pain recovery
- Passing each course's Final Exam grants a hidden **Graduate perk** (`CharacterPerkPerkGroup: "None"`): invisible at character creation, accessible only by completing the course in-run.

#### Weapons
- **Fire-Hardened Spear** — craft from a Straight Branch + any open flame. Full vanilla spear moveset plus a signature **Charred-Tip Strike** attack dealing bonus `Char` damage. Blueprint in the Weapons tab; no tools required.

#### Apparel
- **Cloth Coat** — new clothing item. Blueprint in the Apparel tab.

#### Village Inn overhaul
- **Inn Counter** — new functional interaction card inside the enterable inn. Drop Salt, metal Nuggets, or a Duros Coinage Metal Coin to fund an **Inn Account** (holds up to 500 currency). **Purchase Meal** and **Stay the Night** each draw 50 from the balance. **Work Odd Jobs** (4-hour shift, no balance required) pays 1–2 copper nuggets at 50% quality.

#### Village area
- **Village Crop Fields** — three standalone location cards inside the Village with per-season active harvests: **Flax Field** (spring), **Turnroot Field** (summer), **Rye Field** (autumn). Each field yields a limited number of harvests before it is spent for the season.
- Interior environment cards for the Academy (`CMC_AcademyInterior`) and Inn (`CMC_InnInterior`); dedicated exit cards for each building.
- **Iron Fishing Rod** mechanics split into a dedicated `IronRodFishingPatch`; self-contained and no longer part of the general forage patch.

#### Artwork
- New images: Academy exterior, interior, lectern; per-discipline rooms (Architecture, Armorer, Fishing, Herbalism, Metallurgy, Medicine); Inn desk and interior; village door interior.

#### Framework / data
- `DropInjections.json` — Wild Garlic and Old Growth Bark forage now handled declaratively by the framework.
- `InjectImprovementInto.json` — improvement injection for village improvements now handled declaratively.

### Changed

- `ForageInjectionPatch.cs` heavily trimmed; Wild Garlic, Old Growth Bark, and herbalism bonuses extracted to `HerbalismForagePatch.cs` and `DropInjections.json`.
- `VillageFarmSeasonalCropPatch.cs` removed — replaced by the three standalone seasonal location cards above.
- `RiverBridgeImprovementPatch.cs` removed — improvement injection now handled by `InjectImprovementInto.json`.
- Academy and Inn buildings now each have a dedicated CT4 interior + CT8 exit pair, matching the enterable-cabin pattern used by vanilla.
- `CardVisualsRefresh` helper added — keeps the displayed balance on lecterns and counters accurate after an account balance changes.
- `CurrencyValue` helper introduced — centralises currency amount calculation (Salt, metal Nuggets, Duros coin) in one place.

---

*Previous release: v1.8.1 (2026-06-23)*
