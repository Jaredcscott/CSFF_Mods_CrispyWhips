# CSFF Mod Template

A clean, buildable starter template for **Card Survival: Fantasy Forest** mods.
Built on the **CSFFModFramework** (v2.x) by crispywhips.

After setup you will have a working mod containing:
- One foraged herb item (CT0) with spoilage and an Eat action
- One construction blueprint (CT7) gated on finding the herb
- One placed structure (CT2) as the blueprint result
- One character creation perk (CT0) that starts the player with 3 herbs
- English and stub Chinese localization

**Full companion ebook:** [How to Mod Card Survival: Fantasy Forest](https://crispywhips.itch.io/csff-modding-guide) — covers every system shown here in depth.

---

## Requirements

- **Game:** Card Survival: Fantasy Forest (EA 0.65+)
- **BepInEx:** 5.4.x installed in the game folder
- **CSFFModFramework:** 2.x ([Nexus Mods](https://www.nexusmods.com/cardsurvivalfantasyforest))
- **IDE:** Visual Studio 2022 or JetBrains Rider
- **.NET SDK:** 4.8

---

## Quick Setup

1. **Clone or download** this repo
2. **Rename** `TODO_ModName.csproj` to your mod's name (underscores, no spaces)
3. **Work through `TODO.md`** — it lists every placeholder to replace in order
4. **Populate `lib/`** — see § Lib Setup below (git-ignored, must source from game install)
5. **Build Debug** — outputs directly to `BepInEx/plugins/YourMod/`
6. **Test** — look for your startup Info line in `BepInEx/LogOutput.log`

---

## Lib Setup

`lib/` is not included (git-ignored). Populate it from your game install:

**From `BepInEx/core/`:**
```
BepInEx.dll
0Harmony.dll
```

**From `BepInEx/plugins/CSFF_Mod_Framework/`:**
```
CSFFModFramework.dll
```

**From `Card Survival Fantasy Forest_Data/Managed/`:**
```
UnityEngine.dll
UnityEngine.CoreModule.dll
UnityEngine.AnimationModule.dll
UnityEngine.AssetBundleModule.dll
UnityEngine.AudioModule.dll
UnityEngine.ImageConversionModule.dll
UnityEngine.IMGUIModule.dll
UnityEngine.InputLegacyModule.dll
UnityEngine.InputModule.dll
UnityEngine.JSONSerializeModule.dll
UnityEngine.TextRenderingModule.dll
UnityEngine.UI.dll
UnityEngine.UIModule.dll
UnityEngine.VideoModule.dll
Unity.TextMeshPro.dll
DOTween.dll
```

**Create `Assembly-CSharp-nstrip.dll`** using [NStrip](https://github.com/BepInEx/NStrip):
```
nstrip.exe -cg -p Assembly-CSharp.dll
```
Place the output in `lib/`.

---

## File Structure

```
CSFF-Mod-Template/
├── TODO.md                         ← Start here: checklist of every placeholder
├── Plugin.cs                       ← BepInEx entry point (annotated)
├── GlobalUsing.cs                  ← Global using statements
├── TODO_ModName.csproj             ← Build config (rename this!)
├── ModInfo.json                    ← Mod metadata (4 fields only)
├── BlueprintTabs.json              ← Registers blueprints into crafting journal tabs
├── Patcher/
│   └── GameLoadPatch.cs            ← Runtime data injection hook (delete if JSON-only)
├── CardData/
│   ├── Item/MyHerb.json            ← Example CT0 item
│   ├── Blueprint/Bp_MyHerbFrame.json ← Example CT7 blueprint
│   └── Location/MyHerbFrame.json   ← Example CT2 placed structure
├── CharacterPerk/
│   └── Perk_MyHerbalist.json       ← Example character creation perk
├── Localization/
│   ├── SimpEn.csv                  ← English strings (authoritative at runtime)
│   └── SimpCn.csv                  ← Chinese strings (optional)
└── Resource/Images/
    └── PLACEHOLDER.md              ← Read this before adding card art
```

---

## The Most Important Rules

**1. Every `*WarpData` field needs a `*WarpType: 3` sibling.**
The framework silently skips fields with no WarpType. No log output. Just nothing.

**2. ModInfo.json: exactly four fields.**
Do NOT add `ModEditorVersion` or `ModLoaderVerison` — these cause the Pikachu
ModLoader to process your mod, creating duplicate instances that break blueprint
research on every load.

**3. Blueprint tab keys must be exact.**
`BlueprintTabs.json` keys are LocalizationKey strings, not file names. The two
are different in many vanilla tabs. Use the table from the ebook Appendix B.

**4. LogDebug is invisible.**
BepInEx suppresses Debug-level output by default. During active debugging, use
`Logger.LogInfo()`. Only demote to `LogDebug` after confirming the fix works.

**5. Check your deployed DLL mtime before concluding a fix failed.**
Open `BepInEx/plugins/YourMod/YourMod.dll` in Explorer and check Date Modified.
If it's older than your source edit, the build didn't deploy. Fix the build, not the code.

---

## Version History

| Version | Notes |
|---------|-------|
| 1.0.0 | Initial template release |

---

**Author:** crispywhips  
**License:** MIT  
**Companion ebook:** [How to Mod Card Survival: Fantasy Forest](https://crispywhips.itch.io/csff-modding-guide)
