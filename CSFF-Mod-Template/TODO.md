# Setup Checklist — CSFF Mod Template

Complete these in order. Estimated time: 30–60 minutes.

---

## Step 1 — Rename the Project

- [ ] Decide your mod's folder/assembly name. Use underscores, no spaces. Example: `Fancy_Flowers`
- [ ] **Rename** `TODO_ModName.csproj` to `YourModName.csproj`
- [ ] Open `TODO_ModName.csproj` and change:
  - `<RootNamespace>TODO_ModName</RootNamespace>` → `<RootNamespace>YourModName</RootNamespace>`
  - `<AssemblyName>TODO_ModName</AssemblyName>` → `<AssemblyName>YourModName</AssemblyName>`
  - Debug `<OutputPath>...\plugins\TODO_ModName\</OutputPath>` → your actual plugin folder name

---

## Step 2 — Fill in Plugin.cs

- [ ] `PluginGuid` → `"yourname.your_mod_name"` (lowercase, globally unique, dots OK)
- [ ] `PluginName` → display name (e.g., `"Fancy Flowers"`)
- [ ] `PluginVersion` → `"1.0.0"` (keep in sync with ModInfo.json and README)
- [ ] Change `namespace TODO_ModName;` → `namespace YourModName;`
- [ ] Rename the `ApplyPatch` call if you renamed `GameLoadPatch`, or delete it if JSON-only

---

## Step 3 — Update ModInfo.json

- [ ] `"Name"` → display name (same as PluginName)
- [ ] `"Author"` → your name
- [ ] `"Version"` → same as PluginVersion
- [ ] `"Description"` → one or two sentences of what the mod adds

**WARNING:** Do NOT add `ModEditorVersion` or `ModLoaderVerison` fields.
These cause Pikachu ModLoader to process your mod and break blueprint research
persistence on loads. The four fields above are the ONLY allowed fields.

---

## Step 4 — Replace Placeholder Content

### Replace "My Herb" with your actual item

In `CardData/Item/MyHerb.json`:
- [ ] `"UniqueID": "TODO_modname_my_herb"` → `"yourmod_your_item_name"` (all lowercase, underscores)
- [ ] Rename the file to match your item name (e.g., `FancyFlower.json`)
- [ ] Update `CardName.LocalizationKey` and all other LocalizationKey fields to match
- [ ] Update `CardName.ParentObjectID` and `CardDescription.ParentObjectID` to match UniqueID
- [ ] Update `CardImageWarpData` to the filename of your card art PNG (without `.png`)
- [ ] Adjust spoilage MaxValue (288 = 3 days; 96 DTP = 1 day)
- [ ] Adjust Eat DA stat values:
  - `"930cf914322e9f145af1315d96f85a28"` = Satiation (hunger)
  - `"7d345fd0f1ba4b440b1ca5190f6eb1b9"` = Fullness_Stomach
  - `"278d5a1a68f65cb4883b775e8492464f"` = Calories_Stomach
- [ ] `TransformIntoWarpData: "25a487b16088c2046a51935973ba6a90"` = vanilla RottenRemains (keep this GUID)

**WarpType reminder:** Every `*WarpData` field MUST have a matching `*WarpType: 3` sibling.
If you add a new WarpData field, add its WarpType immediately or the framework silently skips it.

### Replace the Blueprint

In `CardData/Blueprint/Bp_MyHerbFrame.json`:
- [ ] `"UniqueID"` → your blueprint UID (prefix with your mod name)
- [ ] Update all LocalizationKeys and ParentObjectIDs
- [ ] `CardsOnBoard[0].CardWarpData` → the UID of the item that triggers discovery
- [ ] `RequiredElements[0].RequiredCardWarpData` = Plank GUID (`57460207bbf77fa4fb6720aed5d84851`) — keep or replace
- [ ] `BlueprintResult[0].DroppedCardWarpData` → the UID of what this blueprint builds (CT2 location card)
- [ ] Update `CardImageWarpData` to your blueprint's sprite name

In `CardData/Location/MyHerbFrame.json`:
- [ ] `"UniqueID"` → must match `DroppedCardWarpData` in the blueprint
- [ ] Update CardName, CardDescription, LocalizationKeys

### Replace the Perk

In `CharacterPerk/Perk_MyHerbalist.json`:
- [ ] `"UniqueID"` → your perk UID
- [ ] `AddedCardsWarpData` → the UIDs of items the player starts with
- [ ] `PerkIconWarpData` → PNG filename for the perk icon (NOT a card GUID or UniqueID)
- [ ] `SunsCost` → perk cost in the character creation screen
- [ ] `DifficultyRating` → negative = easier (benefits), positive = harder (drawbacks)
- [ ] `CharacterPerkPerkGroup` → the tab GUID. Current value = Situational (default).
  - Perk tab GUIDs are in the ebook Appendix B.

---

## Step 5 — Update Localization

In `Localization/SimpEn.csv`:
- [ ] Replace all `TODO_ModName_*` key prefixes with your actual mod name prefix
- [ ] Update all values to match your actual item/blueprint/perk names and descriptions
- [ ] CSV format: NO header row. Each line: `Key,Value` or `Key,"Value with, commas"`.
  For multi-line descriptions: use actual newlines inside quoted fields, NOT `\n`.

Optional: Update `Localization/SimpCn.csv` with Chinese translations.
The game is ~50% Chinese players — worth adding.

---

## Step 6 — Update BlueprintTabs.json

- [ ] Replace `"TODO_modname_bp_my_herb_frame"` with your blueprint's actual UniqueID
- [ ] Replace the tab key if needed. Current: `"Tab_1_Survival_Subtab_2_Support_TabName"` (Survival › Support).
  The full tab key table is in the ebook Appendix B.

**WARNING:** Tab LocalizationKey ≠ filename in many cases. Always copy the key from
the table — never guess from the filename.

---

## Step 7 — Add Card Art

- [ ] Place PNG files in `Resource/Images/`
- [ ] Filenames must match the values in your `CardImageWarpData` and `PerkIconWarpData` fields
- [ ] See `Resource/Images/PLACEHOLDER.md` for format requirements

---

## Step 8 — Set Up lib/ (Not Tracked by Git)

The `lib/` folder contains DLLs you must source from your game install. It is git-ignored.

1. Copy from `BepInEx/core/`:
   - `BepInEx.dll`
   - `0Harmony.dll`
2. Copy from `BepInEx/plugins/CSFF_Mod_Framework/`:
   - `CSFFModFramework.dll`
3. Copy from `Card Survival Fantasy Forest_Data/Managed/`:
   - `UnityEngine.dll` and all other `UnityEngine.*.dll` files
   - `Unity.TextMeshPro.dll`
   - `DOTween.dll`
4. **Create Assembly-CSharp-nstrip.dll:**
   - Download NStrip from its GitHub repo
   - Run: `nstrip.exe -cg -p Assembly-CSharp.dll`
   - Place the output in `lib/`

---

## Step 9 — Build and Test

1. Open the solution in Visual Studio or Rider
2. Set configuration to **Debug**
3. Build — the output goes directly to `BepInEx/plugins/YourMod/`
4. Launch the game
5. Open `BepInEx/LogOutput.log`
6. Look for exactly one Info line: `"Your Mod Display Name v1.0.0 loaded."`
7. Start a new game, take the Herbalist perk, verify the herb appears
8. Find the herb on the board, verify the Eat action works
9. Research the blueprint, build the Herb Drying Frame

---

## Common First-Mod Failures

| Symptom | Cause | Fix |
|---------|-------|-----|
| Item/blueprint not appearing | WarpType key missing on a WarpData field | Add `"*WarpType": 3` sibling to every `*WarpData` field |
| Blueprint tab missing or garbled | Wrong LocalizationKey in BlueprintTabs.json | Copy key exactly from Appendix B table |
| "Plugin not loaded" | ModEditorVersion or ModLoaderVerison in ModInfo.json | Remove those fields entirely |
| Perk icon is blank | PerkIconWarpData contains a card GUID | Change to a PNG filename string (no extension) |
| No log output from diagnostics | LogDebug used instead of LogInfo | Change to Logger.LogInfo during active investigation |
| Fix "didn't work" | Old DLL still deployed | Check the plugin folder DLL mtime vs your source |
