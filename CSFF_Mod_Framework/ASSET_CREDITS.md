# Asset Credits

Artwork shipped in this package, with its source and terms.

## Card art

| File | Cards using it | Source | Terms |
|---|---|---|---|
| `Resource/Picture/Portal_Kit.png` | `csffmfwportalkit` (item), `csffmfw_bp_portal_kit` (blueprint) | Chiwei, community contribution received 2026-09-22 | see "Terms" below |
| `Resource/Picture/Portal_Placed.png` | `csffmfwportalplaced` (placed structure), `csffmfw_hub_exit` | Chiwei, community contribution received 2026-09-22 | see "Terms" below |
| `Resource/Picture/ACT_MetalNails_*.png` (six metals) | the Metal Nail (`advanced_copper_tools_copper_nails`), one per Metal Type | Chiwei, set `Advanced_Copper_Tools_0928` received 2026-09-28 (moved here from Advanced Copper Tools with the nail, framework 2.28.0) | see "Terms" below |
| `Resource/Picture/CopperNails.png`, `Resource/Picture/IronNails.png` | the copper and iron nail blueprints (`Bp_CopperNails`, `Bp_CastCopperNails`, `Bp_IronNails`), and Water-Driven Infrastructure's copper rivet, which names `CopperNails` | Chiwei: byte copies of `ACT_MetalNails_Copper.png` and `ACT_MetalNails_Iron.png` (owner gallery review 2026-10-12, "use the chiwei image"), replacing earlier AI-generated nails of the same names | see "Terms" below |
| `Resource/Picture/CSFFMFW_StearinCake.png` | the Stearin Cake (`APE_StearinCake`) | derived from vanilla's own `Fat` sprite, recoloured to pale wax by `CSFFModFramework/.art/make_stearin_cake.py` (framework 2.29.0). Chiwei's Additional Processing Equipment draws her cake the same way; her file is NOT copied (no grant covers it) | vanilla card art, recoloured: the same standing as the liquid bowls (owner 2026-10-11); provisional until the art question with Chiwei is answered (fleet plan section 8) |
| `Resource/Picture/ACT_MetalSheet_Copper_Heated.png`, `ACT_MetalSheet_GhostBronze_Heated.png`, `ACT_MetalNails_Copper_Heated.png`, `ACT_MetalNails_GhostBronze_Heated.png` | the heated rows of the Metal Sheet and the Metal Nail (framework 2.29.0); the copper picture also serves the tin, tin bronze, white bronze and iron heated rows | derived from Chiwei's own cold sheet and nail pictures (set `Advanced_Copper_Tools_0928`), given a forge glow by `CSFFModFramework/.art/make_heated_metal.py`. Chiwei's Additional Processing Equipment ships heated pictures of its own; those files are NOT copied (no grant covers them) | see "Terms" below (the source pictures are covered); provisional until the art question with Chiwei is answered (fleet plan section 8) |

Both files replaced earlier framework art of the same name that had shipped since 2026-06-19.
`Portal_Placed.png` was delivered at 896x1344 and downscaled to the 512x768 card canvas; the kit
was delivered at 512x768 and ships exactly as received. Layered PSD sources were supplied with the
drop and are held outside this repository.

## Terms

Chiwei granted free use of this artwork in the Raptor Studios CSFF mod suite, including its
public Nexus releases, with attribution. Confirmed by the maintainer 2026-09-22. Attribute as
"Artwork by Chiwei". This grant covers use within the suite; it is not a transfer of
copyright and does not license the artwork for use outside these mods.

## Adding artwork

When adding external, commissioned, AI-generated, or derived artwork, record the source,
author/tool, licence or terms, and any attribution text here **in the same commit** that adds the
file. This file previously claimed the framework shipped no custom artwork; that claim was written
on 2026-04-27 and had been false since the portal art landed on 2026-06-19, because the art was
added without touching this file.
