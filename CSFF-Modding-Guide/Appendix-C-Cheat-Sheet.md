# Appendix C — One-Page Cheat Sheet

> Print this and pin it to your monitor. These are the 10 most common first-mod failures
> and how to fix them immediately.

---

## The 10 Beginner Traps

### 1. Missing WarpType Sibling
**Symptom:** Item/blueprint/perk appears in log but not in game. No error output.  
**Cause:** `*WarpData` field present but `*WarpType` sibling is missing.  
**Fix:** Every `*WarpData` key MUST have a matching `*WarpType: 3` key directly next to it.

```json
"CardTagsWarpData": ["tag_Plateable"],
"CardTagsWarpType": 3
```

---

### 2. ModInfo.json Forbidden Fields
**Symptom:** Blueprint research resets on every load. Items may duplicate.  
**Cause:** `ModEditorVersion` or `ModLoaderVerison` present in ModInfo.json causes Pikachu ModLoader to process your mod, creating duplicate instances.  
**Fix:** ModInfo.json must have **exactly four fields**: `Name`, `Author`, `Version`, `Description`. Delete any other fields.

---

### 3. Blueprint Tab Key Wrong
**Symptom:** Blueprint doesn't appear in the crafting journal.  
**Cause:** `BlueprintTabs.json` key doesn't exactly match the tab's LocalizationKey (filename ≠ key in many tabs).  
**Fix:** Copy the key from Appendix B. Never guess from the filename.

---

### 4. WarpData Sprite Field Gets a GUID
**Symptom:** Card has no image, perk has no icon. No error.  
**Cause:** `CardImageWarpData`, `CardBackgroundWarpData`, or `PerkIconWarpData` contains a 32-char GUID or UniqueID instead of a sprite name string.  
**Fix:** Use the PNG filename without extension (e.g., `"MyHerb"`, not the card's GUID).

---

### 5. LogDebug Is Invisible
**Symptom:** You add diagnostic log lines, restart the game, see nothing in the log.  
**Cause:** BepInEx suppresses `Debug`-level messages by default.  
**Fix:** During active investigation, always use `Logger.LogInfo()`. Demote to `LogDebug` only after confirming the fix works.

---

### 6. Stale Deployed DLL
**Symptom:** Your "fix" does nothing; the game behaves as before.  
**Cause:** The DLL in `BepInEx/plugins/YourMod/` is an older build.  
**Fix:** Check the DLL's Date Modified in Explorer. If older than your last source edit, the build didn't deploy. Fix the build output path in your .csproj, then rebuild.

---

### 7. DismantleAction vs CardInteraction Confusion
**Symptom:** Self-action button doesn't appear, or dragging an item doesn't work.  
**Cause:** Using the wrong action type.  
**Fix:**
- Self-action buttons (Eat, Pet, Rest, etc.) → `DismantleActions`
- Drag-and-drop interactions (drag food to feed, drag tool to grind) → `CardInteractions`
- A self-action in `CardInteractions` with empty `CompatibleCards` shows **no buttons**

---

### 8. EdibleStats Without a DismantleAction
**Symptom:** Food item exists but no Eat button appears.  
**Cause:** `EdibleStats` alone does not create action buttons in CSFF.  
**Fix:** Add an explicit `DismantleAction` with `ReceivingCardChanges.ModType: 3` (destroy item). Copy the 4 standard food stat GUIDs from Appendix B.

---

### 9. WaitForSeconds in a Coroutine
**Symptom:** Mod coroutine hangs the game or never completes.  
**Cause:** CSFF sets `timeScale = 0` during player-action waits and fade transitions. `WaitForSeconds` is time-scale dependent — it stalls indefinitely at `timeScale = 0`.  
**Fix:** Use `yield return null` (frame-by-frame) or `WaitForSecondsRealtime` instead.

---

### 10. InventorySlotsWarpData Uses `tag_*` Instead of `GpTag_*`
**Symptom:** Inventory filter doesn't accept items you expect it to.  
**Cause:** `InventorySlotsWarpData` resolves to `CardTabGroup[]` type — it requires `GpTag_*` prefix strings, not `tag_*`.  
**Fix:** Change `"tag_Ingredient"` → `"GpTag_Ingredient"` (verify the GpTag actually exists in vanilla first — 137 exist total).

---

## Quick Reference

| CardType | Meaning |
|----------|---------|
| 0 | Item |
| 2 | Placed structure |
| 4 | Environment (world node) |
| 7 | Blueprint |
| 8 | Explorable location |
| 9 | Liquid |
| 10 | Environment improvement |

| WarpType | Meaning |
|----------|---------|
| 3 | Reference (most common — use this) |
| 4 | Add |
| 5 | Modify |
| 6 | AddReference |

**5-step debug protocol:**
1. Add `Logger.LogInfo()` lines that dump actual runtime state
2. Rebuild and confirm the deployed DLL mtime is fresh
3. Restart the game, collect `BepInEx/LogOutput.log`
4. Fix what the log reveals (not what you assumed)
5. Demote diagnostic lines to `LogDebug` after confirming the fix

---

*From "How to Mod Card Survival: Fantasy Forest" — crispywhips.itch.io/csff-modding-guide*
