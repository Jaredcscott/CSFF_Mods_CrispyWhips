# Advanced Copper Tools — Module Notes

These notes cover ACT-specific subsystems. Also read the root `CLAUDE.md` for all general rules.

## Smelting Container Tag & CookingRecipe Stat Mapping
- **`tag_SmeltingContainer`** = vanilla container tag on the Furnace (obfuscated `TextMeshProUGUI_6687`). Any modded structure that smelts vanilla items MUST include `tag_SmeltingContainer` in `CardTagsWarpData`. Without it, vanilla items' PassiveEffects drain Progress at the same rate the CookingRecipe fills it → stat stays at 0.
- **`tag_SmeltingContainerIron`** = vanilla container tag on Forge, Furnace, Bloomery (obfuscated `LayoutElement_6968`). Iron nuggets (SD4=200) drain FuelCapacity -300/DTP when not in a `tag_SmeltingContainerIron` container or below 1000°C. Any modded forge **MUST include BOTH tags** — without the iron tag, iron nuggets drain -400/DTP and never heat. See memory: `reference_iron_nugget_passive_heating`.
- **`tag_SmeltsAt1100`** = tag on ITEMS (not containers) — "I require 1100°C to smelt". Used in InventoryFilter to accept items into a smelting station.
- **Two smelting approaches — NEVER MIX ON THE SAME ITEM:**
  - **SmeltingRecipeInjector** (simple): `SmeltingRecipes.json` in mod root — framework injects CookingRecipes into any `tag_SmeltingContainer` station. Best for flat "smelt → N nuggets" recipes.
  - **Progress-based passive smelting** (advanced): item has `FuelCapacity` + `Progress` + 3 PassiveEffects (drain outside forge, drain below 1100°C, fill above 1100°C). Best for custom OnFull behavior. Full pattern: `Documentation/CSFF_Patterns.md` § Smelting.
- **CRITICAL — Do NOT use both systems on the same item.** WDI gears gave 48 copper instead of 12 from this mistake.

## Metal Type Gating (SpecialDurability4 on MetalNugget / MetalBarFinished)
- `SpecialDurability4` (SD4) encodes **Metal Type**. Default=0. Known values: **Copper=100, GhostBronze=110, Tin=120, TinBronze=130, WhiteBronze=140, Iron=200**.
- To require a specific metal in a blueprint's `RequiredElements`, add `"Special4": { "Active": true, "FloatValue": 200.0, "MaxValue": 200.0 }` (exact match). Use a range `FloatValue < MaxValue` (e.g. 200/1000) for "iron or above".
- Vanilla references: `Bp_RemakeIronBar` (SD4=200 exact), `Bp_CommissionCopperNuggets` (SD4=100), `Bp_Smithed_BlankHammerHead` (SD4=200–1000 range).
- `ProducedCards` / `OnFull` CANNOT set SD4 on spawned bars — use a `GiveCard` postfix. See memory: `reference_givecard_postfix_stat_init`, `reference_metal_type_sd4`.
