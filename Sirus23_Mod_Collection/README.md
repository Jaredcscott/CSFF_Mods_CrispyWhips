# Sirus23 Mod Collection

**Version:** 1.20.2
**Author:** Sirus, Jared
**For:** Card Survival: Fantasy Forest (EA 0.65)
**Requires:** CSFFModFramework 2.5.0+

## Overview

Sirus23 Mod Collection bundles three animal companion systems and a full sheep husbandry chain for Card Survival: Fantasy Forest. Content is loaded through CSFFModFramework, with blueprints registered through `BlueprintTabs.json`, perks loaded from `CharacterPerk/`, and runtime behavior handled by focused Harmony patches.

**Dependencies:** `CSFFModFramework` only (declared `SoftDependency`, load-order only). This mod does not depend on, and is not depended on by, any other in-house content mod.

## Animal Companions

Three companions are available, each unlocked by a matching character-creation perk. All three share the same upkeep model: Hunger (SpoilageTime), Thirst (UsageDurability), and Morale (Progress) drain passively over time. Feed and water them via drag interactions, or let them self-sustain — if a companion's hunger or thirst drops below 20% and a matching food or water source is on the board, it automatically eats or drinks from the nearest one. Pet or play with them as self-actions to restore morale. Neglect causes them to die or flee.

### Wolf Companion

The **Wolf Friend** perk starts the player with a loyal wolf companion. The wolf can be fed, watered, treated, petted, played with, sent hunting, used to track scents, and set to guard camp. Its hunger, thirst, morale, and health are tracked on the card over time. Upkeep ticks run on CSFFModFramework's `TickEvents`, and the food/water item sets come from the framework's per-game-version `VanillaIds` registry.

Wildlife and ambient encounters are suppressed while the wolf is present in the player's current environment via the declarative `EncounterGuards/WolfGuard.json` (evaluated by the framework's encounter guard service). NPC encounters are not suppressed.

**Pack Bond** — a hidden perk granted the first time the wolf's one-time **Bond with Wolf** action (8 DTP) is performed. Grants +15 to the Tracking skill, representing heightened instincts from the bond.

### Fox Companion

The **Fox Friend** perk starts the player with a cunning fox companion. The fox can be fed (meats, fish, small game), given dried-berry treats, watered, petted, played with, sent **Scouting Ahead** (a 3-DTP action that sharpens the player's Tracking skill and returns 1–2 raw meat), and used to **Retrieve Partridge** (a timed hunt returning partridge). The fox has a higher morale drain than the wolf. If neglected it dies; its body remains.

A **Wild Fox** can also be found roaming during the day (6:00–20:00) without taking the perk. Approaching it starts an encounter (it may flee, or scuffle briefly); offering it dried berries (Springberries, Bilberries, or Juniper Berries) directly tames it into the same Fox Companion the perk grants. The wild fox is suppressed while a tamed Fox Companion is on the board (`EncounterGuards/FoxGuard.json`, 35% suppression chance) and respawns some time after the companion is lost. **The equivalent Wild Wolf tame path is a planned follow-up and is not yet implemented — the Wolf remains perk-only for now.**

### Owl Companion

The **Owl Friend** perk starts the player with a silent owl companion. The owl accepts meat and dried-meat treats; it can be petted, assigned **Night Watch** (a 4-DTP rest action that restores sleep and reduces stress), and used to **Retrieve Mouse** for a small foraging return. The owl has a slower hunger and thirst rate than the wolf or fox. Like the wolf, it partially suppresses wildlife and ambient encounters while present (`EncounterGuards/OwlGuard.json`, 35% suppression chance — weaker than the wolf's full suppression). Unlike the wolf and fox, the owl won't follow you into caves or structures — it waits outside and rejoins you when you return to the outdoors. If neglected, it silently flies away — no body remains.

A **Wild Owl** can also be found at night (20:00–6:00), drawn toward the light of your camp. It leaves discoverable tracks (feathers and pellets) in whatever spot it last visited — find them the next morning, with a chance to spot them scaling with your **Tracking** skill. Approaching the owl starts an encounter (it may flee, or fight back); offering it a mouse, minnow, or dead partridge attempts to tame it — success chance scales with your **Tracking** skill (roughly 50% untrained, 75% at 150 Tracking), and a failed attempt has a small chance to provoke the owl into attacking instead of just fleeing. A successful tame grants the same Owl Companion the perk does, and the wild owl disappears for good. The wild owl can also be caught alive in a baited snare. It is suppressed while a tamed Owl Companion is on the board and respawns some time after the companion is lost — killing or catching it in combat instead starts a longer respawn timer.

## Sheep Husbandry

Sheep Husbandry adds sheep, rams, lambs, milking, shearing, wool processing, dairy processing, and supporting structures. The chain includes sheep milk, curdled milk, cream, butter, buttermilk, yogurt, ricotta, sheep cheese, aged sheep cheese, wool, lanolin, yarn, woven cloth, wool clothing and bedding, shears, cheese cloth, butter churn, sheep feeder, sheep pen.

Shearing has a chance to yield **Lanolin**, a waxy byproduct. Blend it with frostleaf powder into a **Lanolin Salve** (blueprint under Survival › Medical, discovered by having Lanolin on the board) — apply it to your skin for a lasting Comfort boost.

Wild sheep and rams are defined with `CardData/Trigger/` JSON. Runtime trigger support is provided by CSFFModFramework's trigger service.

### Tend Flock

The Sheep Feeder's **"Tend Flock"** action shears every ready tame or lactating sheep in the current environment in one click, instead of shearing each sheep by hand — the same Wool threshold, 2x Wool output, and 25% chance of Lanolin as the individual Shear action, just applied to the whole nearby flock at once. **Milking is not automated** — dragging a container onto a Lactating Sheep by hand is still how milk is collected; automating it would require the game's liquid-container attachment mechanism, which has no existing precedent in this mod and was intentionally left out of this pass.

### Prepared Dairy Dishes

Butter, Sheep Cheese, and Cream each have a second use beyond eating raw: drag Butter onto Roasted Cattail Root for **Buttered Roots**, drag Sheep Cheese onto Wheat Roundbread for **Cheese-Stuffed Flatbread**, or drag Cream onto Mashed Turnroot for **Creamy Mash**. Each combination consumes the dairy item and transforms the vanilla dish into the new prepared meal, which offers stronger nutrition than either ingredient alone. **Nutrition values and the recipe pairings are initial balance guesses, not final-tuned numbers.**

### Sheep Pen (pen-or-perish)

The **Sheep Pen** (`sh_sheep_pen`, blueprint `sh_bp_sheep_pen` under Farming › Animal Husbandry, 8 Plank + 4 Rope + 4 Stone) is a buildable enclosure that holds up to 4 tame sheep and/or rams as storage — drag them in, and their wool regrowth, milk, and ram-proximity breeding continue normally while penned (confirmed via decompile: a card's own `AlwaysUpdate` flag, which tame sheep already carry, keeps applying regardless of container nesting).

**Pen or perish:** every in-game night, any tame sheep, ram, or lactating sheep left OUTSIDE a pen (in the environment the player is currently standing in) risks being lost — either wandering off, or taken by a predator overnight (which leaves a **Sheep Remains** card behind so the loss is never a silent mystery). A **Wolf Companion** present in that environment stands guard and suppresses the roll entirely for every unpenned sheep that night. Penned sheep are always exempt. **The escape and predation percentages are conservative starting placeholders, not final-tuned balance values** — they may be adjusted after playtest feedback. Picking the pen back up (its "Pick Up" action) safely releases any sheep inside onto the ground first rather than destroying them.

The escape/predation check only evaluates whichever environment the player currently occupies when a night passes — a flock left at a completely different, unvisited environment is not at risk until the player is present there again.

## Felt Working

The felt pathway provides an alternative to the yarn and weaving chain. Press 4 raw Wool into a sheet of Felted Wool, then use felted wool to craft a Felt Hat (head, +8 Cold Resistance), Felt Mittens (hands, +5 Cold Resistance), Felt Vest (outer torso, +12 Cold Resistance — shares the outer-torso slot with the Wool Tunic), Felt Boots (outer shoes, +8 Cold Resistance, also requires Twine), or knit 2 Yarn directly into Wool Socks (inner shoes, +5 Cold Resistance). Felt 3 Felted Wool into a **Felt Bedroll** — a bedding item, like the Wool Blanket, but a distinctly stronger one: it grants a bigger sleep-insulation bonus than the blanket (+16 vs. +10), at the cost of more carry weight (900g vs. 700g). All seven blueprints unlock from the Cloth subtab of the Tailoring tab.

Felt Mittens, Felt Vest, and Felt Bedroll's stat values are an initial balance guess, not final-tuned numbers.

## Crafting Tabs

Blueprints are mapped through `BlueprintTabs.json` and injected by CSFFModFramework. The collection does not ship `ScriptableObject/CardTabGroup/` overrides.

## Development Notes

This project targets .NET Framework 4.8. It uses shared assembly references from `../HerbsAndFungi/lib/` for local builds, including `0Harmony.dll`, `BepInEx.dll`, Unity assemblies, and `Assembly-CSharp-nstrip.dll`. A local `lib/` junction may also point at that shared folder for audit tools that expect a per-mod lib directory.

Use `Development_Tools/Deploy-Mods.ps1 -CSFFModFramework -Sirus23ModCollection` to build and deploy the framework plus this mod. The deploy script preserves the trigger JSON subfolder required by CSFFModFramework.
