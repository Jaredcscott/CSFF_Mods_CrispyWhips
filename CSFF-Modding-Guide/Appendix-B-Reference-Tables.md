# Appendix B — Reference Tables

> Verified against game data EA 0.65g. GUIDs are stable across minor updates.
> When in doubt, cross-check against
> `Documentation/GameData/CSFF-JsonData_EA_0-65g/UniqueIDScriptableGUID/`.

---

## CardType — Full Enum

| CardType | Meaning | Vanilla Count (EA 0.65) | Examples |
|---|---|---|---|
| 0 | Item (incl. animals-as-cards, weather curses) | 1,078 | `DogFriend`, `AcornFlatbread` |
| 1 | Enclosure animal variant | 9 | `GoatEnclosureFemale` |
| 2 | Placed structure / location card | 327 | `AlembicOn`, `BedImprovised` |
| 3 | Event card (encounter wrapper) | 322 | `Combat_EventBear_1_Explore` |
| 4 | **Environment** (world location node) | 153 | `Env_BearCave`, `Env_Cabin` |
| 5 | Weather | 11 | `Weather_2_LightRain` |
| 7 | Blueprint (incl. rituals/enchantments/blessings) | 450 | `Bp_AwakeningRitual` |
| 8 | Construction location / explorable sub-location | 153 | `Cabin`, `Caves_BearCave` |
| 9 | Liquid | 142 | `LQ_AlderTanningLiquor` |
| 10 | Environment improvement | 100 | `Imp_CabinConstructionLocationAtticEast` |
| 11 | Environment damage | 3 | `Dmg_RoofCollapsed` |
| 13 | Invisible helper / enricher | 8 | `BlessedGroveInvisible` |

**Common pitfalls:**
- CT4 = Environment (NOT "Buff/Effect")
- CT8 = outdoor explorable (NOT CT2)
- There is no CardType 6
- There is no `BlueprintData` type — blueprints are CT7 `CardData`

---

## Vanilla Item GUIDs

Vanilla items in `RequiredCardWarpData`, `CardsOnBoard`, `CompatibleCards.TriggerCardsWarpData`,
etc. require **GUID hashes**, not human-readable names.

| Item | GUID |
|------|------|
| Stone | `a7384e5147b23a642809451cc4ef24fb` |
| Wood | `692afc638c39e32428629da58f56136a` |
| Rope | `a7a58aa687df66e47a42fc13e0fdbeaa` |
| Plank | `57460207bbf77fa4fb6720aed5d84851` |
| StickLong | `3db4c94184af274409f0d3eb16870f64` |
| Clay | `68c14d265ea6c874ba79444d2e1ef7b3` |
| ClayBowl | `a968f3eaffc6b9743b82982b5af2ab8c` |
| ButterChunk | `6d12c7ac4b8baf24fabeeedd77dc60da` |
| MetalNugget | `4b0f4937a5ecb90499428c8c10288afc` |
| ForgeHammer | `e118b8cd90f14b048aab78a0d37e8f61` |
| RottenRemains | `25a487b16088c2046a51935973ba6a90` |

> There is no "WoodenBowl" in vanilla — only `ClayBowl`, `ClayPlate`, `WoodenPlate`.

### Wildlife Prey & Companion Food GUIDs

| Item | Display Name | GUID |
|------|-------------|------|
| RawMeat | Raw Meat | `fe07d4d800bcc8646a0ff2513c78d5df` |
| MouseRaw | Dead Mouse | `cea21fa218c28ee49a49ba2ffc8eba7c` |
| SquirrelRaw | Dead Squirrel | `e32cb25589e676c49baa6c8f54637fa3` |
| Dead Partridge | Dead Partridge | `f72ca8f6249870d4b9306c2d779c93a9` |
| Dead Hare | Dead Hare | `81766d95f54c99242ab3d18be522db49` |
| Bones | Bones | `674b640f46671f1418af4559b259b442` |
| MeatDried | Dried Meat | `f4be79e87ba98db41a9c9e31bb76c33d` |
| SpringberriesDried | Dried Springberries | `0f14d75156adbeb42b1e7616414c236b` |
| BillberriesDried | Dried Billberries | `9955af347ce41af4c9e78ee97f008182` |
| JuniperBerriesDried | Dried Juniper Berries | `4568ba3bab979cf43b79b828c331b514` |

> There is no singular `"Bone"` (it's `Bones`), no `"DryBerries"`, no `"DriedMeat"` (it's `MeatDried`).

**Finding other GUIDs:** Grep `Documentation/GameData/CSFF-JsonData_EA_0-65g/UniqueIDScriptableJsonDataWithWarpLitAllInOne/CardData/<Name>.json` for a specific item.

---

## Food Stat GUIDs

These four GUIDs are copy-paste ready for any food item's `DismantleAction.StatModifications`:

| Stat | GUID |
|------|------|
| Satiation (Hunger) | `930cf914322e9f145af1315d96f85a28` |
| Hydration (Thirst) | `95ca7c21ffad5e647acc3d9cb5bfcde6` |
| Fullness_Stomach | `7d345fd0f1ba4b440b1ca5190f6eb1b9` |
| Calories_Stomach | `278d5a1a68f65cb4883b775e8492464f` |

---

## Vanilla Stat GUIDs

For `StatWarpData` fields. GUIDs are preferred over names because GameStat keys carry CJK annotations.

| Stat | GUID |
|------|------|
| Filth | `111606ba528c30e4f8e38494f41b92c7` |
| Pain | `ec5f330267076884dbe9cfdd2fd8503b` |
| Nausea | `547f09c7ea6d11d4bbf6714255b0dfd5` |
| Stress | `3b79a4c6d7e151044a1c56fbbd401d78` |
| FoodPoisoning | `8080df0dc107caa47b9591fde9347e09` |

Other stats: `Documentation/GameData/CSFF-JsonData_EA_0-65g/UniqueIDScriptableGUID/GameStat.json`.

---

## Vanilla Tool Tag GUIDs

| Tag | GUID |
|-----|------|
| `tag_Axe` | `825bac794179a8c4eb1cf08fd3560754` |
| `tag_AdvancedAxe` | `a62f25f8900c00f429fd09c42be7b76d` |
| `tag_WoodCutting` | `0fe2604ce00a351478a12b54dede7c7d` |
| `tag_Cutter` | `09a85f061d2363f4bb0927eeba3d65dd` |

Common tag name strings (no GUID needed when used as `tag_*` in CardTag array slots):
`tag_Hammer`, `tag_Cutter`, `tag_GrindingTool`, `tag_HammeringToolGeneral`, `tag_Plateable`,
`tag_Fire`, `tag_FireSource`, `tag_River`, `tag_EnvIndoors`, `tag_DryingRackSanctuary`,
`tag_Dryable`, `tag_CookingContainer`, `tag_ContainerSealed`, `tag_WaterContainer`,
`tag_Light`, `tag_Structure`, `tag_Vegetable`, `tag_HumanFood`, `tag_Foraged`.

---

## Vanilla Liquid GUIDs

| Liquid | GUID | Notes |
|--------|------|-------|
| `LQ_Water` | `425259cb06b869d45be2e7f1b5b54aff` | Cold water. FuelCapacity = "Liquid Temperature" (max 200). At max → `LQ_StewWater`. |
| `LQ_StewWater` | `a0e1cf6d47685a741b5cd9889fb39227` | Boiled water. Supports cooking CIs. Reverts to LQ_Water on cooldown (~30 min). |
| `LQ_WaterUnsafe` | `aa33d3104cc682c4e9a163e7df9a7e13` | Pond/river fill. → LQ_Water on full heat. |
| `LQ_WaterRiver` | `397594d260e114749ab6e1a76a732fbb` | River-fetched. → LQ_Water on full heat. |
| `NOT_USED_LQ_Oil` | `678cb217d7eea1d48a8c61b5f6975655` | **Abandoned.** Corrupted tags. Build mod oil fresh — do not reference. |

**Critical:** Herb soaking CardInteractions trigger on `LQ_Water` only. If a player boils first
(creating `LQ_StewWater`), vanilla herb CIs will silently do nothing.

---

## Blueprint Tab LocalizationKeys

`BlueprintTabs.json` keys must be **exact** string matches. The key is NOT always
derivable from the filename — see ⚠ rows.

| In-game Tab | LocalizationKey |
|-------------|----------------|
| Survival › Fire | `Tab_1_Survival_Subtab_1_Fire_TabName` |
| Survival › Support | `Tab_1_Survival_Subtab_2_Support_TabName` |
| Survival › Medical | `Tab_1_Survival_Subtab_3_Medical_TabName` |
| Survival › Entertainment | `Tab_1_Survival_Subtab_4_Entertainment_TabName` |
| Survival › Cooking | `Tab_1_Survival_Subtab_5_Cooking_TabName` |
| Construction › Working/Basic Tools ⚠ | `Tab_2_Construction_Subtab_1_WorkingTools_TabName` |
| Construction › Metal Tools | `Tab_2_Construction_Subtab_2_MetalTools_TabName` |
| Construction › Advanced Tools ⚠ | `Tab_2_Construction_Subtab_2_AdvancedTools_TabName` |
| Construction › Materials ⚠ | `Tab_2_Construction_Subtab_3_Materials_TabName` |
| Construction › Furniture ⚠ | `Tab_2_Construction_Subtab_4_Furniture_TabName` |
| Construction › House Building ⚠ | `Tab_2_Construction_Subtab_5_HouseBuilding_TabName` |
| Hunting › Fishing | `Tab_3_Hunting_Subtab_1_Fishing_TabName` |
| Hunting › Trapping | `Tab_3_Hunting_Subtab_2_Trapping_TabName` |
| Hunting › Close Combat | `Tab_3_Hunting_Subtab_3_CloseCombat_TabName` |
| Hunting › Marksmanship | `Tab_3_Hunting_Subtab_4_Marksmanship_TabName` |
| Hunting › Coatings | `Tab_3_Hunting_Subtab_5_Coatings_TabName` |
| Tailoring › Improvised | `Tab_4_Tailoring_Subtab_1_Improvised_TabName` |
| Tailoring › Cloth (Clothing) | `Tab_4_Tailoring_Subtab_2_Cloth_TabName` |
| Tailoring › Leather ⚠ | `Tab_4_Tailoring_Subtab_3_Leather_TabName` |
| Tailoring › Skin Patching ⚠ | `Tab_4_Tailoring_Subtab_4_SkinPatching_TabName` |
| Tailoring › Equipment ⚠ | `Tab_4_Tailoring_Subtab_5_Equipment_TabName` |
| Tailoring › Tools ⚠ | `Tab_4_Tailoring_Subtab_6_Tools_TabName` |
| Metal & Clay › Utensils | `Tab_5_MetalAndClay_Subtab_1_Utensils_TabName` |
| Metal & Clay › Tools | `Tab_5_MetalAndClay_Subtab_2_Tools_TabName` |
| Metal & Clay › Metal Crafts | `Tab_5_MetalAndClay_Subtab_3_MetalCrafts_TabName` |
| Farming › Agriculture | `Tab_6_Farming_Subtab_1_Agriculture_TabName` |
| Farming › Animal Husbandry | `Tab_6_Farming_Subtab_2_AnimalHusbandry_TabName` |
| Magic › Shrines | `Tab_7_Magic_Subtab_1_Shrines_TabName` |
| Magic › Implements | `Tab_7_Magic_Subtab_2_Implements_TabName` |
| Magic › Calling Rituals | `Tab_7_Magic_Subtab_3_CallingRituals_TabName` |
| Magic › Rituals | `Tab_7_Magic_Subtab_4_Rituals_TabName` |

⚠ = the filename and the actual LocalizationKey disagree. Always use the key from this table.

**Never ship `ScriptableObject/CardTabGroup/` JSON files** — they override vanilla tabs for all mods.

---

## Perk Tab GUIDs (`CharacterPerkPerkGroup`)

| Tab | GUID | Theme |
|-----|------|-------|
| Sex | `9be1b5be8f64df444b73828b99159b0c` | Gender / sex-specific traits |
| Physical | `f913e8e1e69615943b6dfaa6dba9cda1` | Body build, physical ailments (Aged, Bleeder) |
| Psychological | `335cd7a47a90df5419abc30a4105db1a` | Mind, spirit, fears, sleep (Insomniac, Phobias) |
| Physiological | `5d894fd43dd840949ae17ae1bda18bbc` | Innate bodily functions, genetic traits |
| **Situational (default)** | `72120cda8e1cef540b1b25118dd7edaa` | World-state, circumstances |
| Knowledge | `aacb8df12eedb7b48a9801360ae638c7` | Skills, professions, backgrounds |

When `CharacterPerkPerkGroup` is absent or empty, perks default to **Situational**.

---

## WarpData Field → Type Mapping

The framework resolves `*WarpData` based on the target field's reflected type.
Wrong token = **silent failure** (logged as `<value>(<ExpectedType>)` unresolved, or no log at all).

| Field | Accepted Values |
|-------|----------------|
| `InventorySlotsWarpData` | `GpTag_*` strings ONLY. `tag_*` is wrong. |
| `CardTagsWarpData` | `tag_*` strings |
| `TriggerTagsWarpData` | `tag_*` strings |
| `TriggerCardsWarpData` | UniqueIDs or 32-char GUIDs |
| `AcceptedCardsWarpData` | UniqueIDs or 32-char GUIDs |
| `DroppedCardWarpData` | UniqueIDs or 32-char GUIDs |
| `RequiredCardWarpData` | UniqueIDs or 32-char GUIDs |
| `TransformIntoWarpData` | UniqueIDs or 32-char GUIDs |
| `OverrideIconWarpData` | Sprite name strings ONLY (`"Hot"`, `"Hunger_Old"`) |
| `CardImageWarpData` | Sprite name strings ONLY |
| `CardBackgroundWarpData` | Sprite name strings ONLY |
| `PerkIconWarpData` | Sprite name strings (PNG filename without extension) |
| `StatWarpData` | GUIDs (preferred) or stat names |
| `EquipmentWarpData` | UniqueIDs |
| `TagWarpData` | `tag_*` strings |

**Sprites NEVER take a CardData GUID.** Use the bare sprite name string.

**WarpType values:** 3 = Reference (most common), 4 = Add, 5 = Modify, 6 = AddReference.
When in doubt, use 3.

---

## CSFFModFramework Plugin GUID

`crispywhips.CSFFModFramework`

Used in `[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]`.
