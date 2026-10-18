# Asset Credits

This mod ships custom project artwork under `Resource/Picture/`.

During the April 26, 2026 compliance pass, no attribution-required third-party image source was identified in the shipped PNG assets.


`CopperArmor.png`, `CopperBracers.png`, `CopperGreaves.png`, and `CopperHemlet.png` were added as user-supplied armor artwork for this mod.

The per-metal card images `ACT_MetalSheet_*`, `ACT_MetalPan_Shaped_*`, `ACT_MetalPan_Wearable_*`, `ACT_MetalStoveTop_*`, `ACT_MetalCauldron_*`, `ACT_LargeSaw_*`, `ACT_MetalNails_*`, `ACT_MetalStove_Off_*` and `ACT_MetalStove_On_*` were drawn by Chiwei (a Nexus Mods player) and sent to this mod on 2026-09-28 as the set `Advanced_Copper_Tools_0928`, for use in this mod. `ACT_MetalPan_Shaped_*` includes the heated pan, `ACT_MetalPan_Shaped_Heated`. Every one ships on the 512x768 card canvas with transparency; the first batch (the sheet, pans, stove top, cauldron and the iron nails and saw) was finished with GIMP (the 600x800 cauldrons were padded on top, not cropped). The layered `.psd` sources are kept in `.art/chiwei_0928/` and are not shipped.

The per-metal lantern images `ACT_MetalLantern_Off_*`, `ACT_MetalLantern_On_*`, `ACT_MetalLantern_Oilwell_Off_*` and `ACT_MetalLantern_Oilwell_On_*` (copper, bronze, tin, iron, ghost bronze, white bronze) were drawn by Chiwei and sent to this mod on 2026-10-05 as the set `Advanced_Copper_Tools_1004`, for use in this mod. They arrived as 512x768 RGBA and ship framed on the 512x768 card canvas (reframed 2026-10-14, `d88bbd515`). Since 1.18.10 the iron images draw the Iron Lantern and Iron Lantern Oilwell. The Maya and PSD sources stay with the sender's package and are not in the repo.

Since the 2026-10-12 owner art review, the Small Copper Stove, Large Saw (copper and iron), Forged Pan Blank, Wearable Metal Pan, Copper Cauldron, Metal Lantern and Lantern Oilwell blueprints, and the Large Saw perk icon, draw Chiwei's copper (or iron) images above instead of their earlier pictures, which were removed.

`ACT_CalmingTea.png`, `ACT_FocusTea.png`, `ACT_WarmingTea.png` and `ACT_Oil.png` are recoloured copies of the game's own `Bowl_Clay_Water` sprite (Card Survival: Fantasy Forest, EA 0.68b), made with `Development_Tools/CardArt/liquid_bowl.py`; only the liquid surface colour was changed.

The tea station (`Tea_Station_Placed/Lit/Kit`), copper bathtub (`CopperBathtub_Empty/Full_Unlit/Warm/Kit`), `CopperSpigot` and `ToolBelt` pictures were regenerated 2026-10-12 with OpenArt (Nano Banana 2, image-to-image) using Chiwei's copper stove, the game's own sprites and this mod's tapped-tree art as references; provenance per image is in `.art/<ImageName>.json` (not shipped).

When adding external, commissioned, AI-generated, or derived artwork, record the source, author/tool, license or terms, and any attribution text here before publishing the mod package.