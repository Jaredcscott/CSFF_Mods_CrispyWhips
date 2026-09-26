# Asset Credits

Artwork shipped in this package, with its source and terms. Counts measured 2026-09-26 against
`Resource/Picture/` (183 PNG files).

## Chiwei, received 2026-09-22 (53 files)

Perk and trait-status icons, contributed as PNG with layered PSD sources. The PSDs were supplied
with the drop and are held outside this repository.

- **37 perk icons** (`CMC_Pk_*.png`, `CMC_Perk_DeadlyDisease.png`), referenced from
  `CharacterPerk/*.json` via `PerkIconWarpData`. Each replaced a borrowed vanilla sprite.
- **16 trait-status icons**, referenced from `GameStat/CMC_Trait*.json` via `Statuses[].IconWarpData`,
  covering 18 status bands across Agoraphobia, Moon-Bound, Sun-Scorched, Sunlight Exposure and
  Nyctophobia. Nyctophobia's four bands share two images, by the contributor's own file naming.

All 53 were delivered as RGBA with the background already keyed. `CMC_Pk_Trapper.png` and
`CMC_Pk_Troglodyte.png` arrived at card-art resolution (896x829 and 896x1344) and were downscaled
to a 512px long edge, since a perk icon renders small; the other 51 ship exactly as received.

Chiwei also contributed the Nightcrawler trait design shipped in 1.68.36.

## Chiwei, received 2026-09-24 (2 new files, 5 replaced)

Sent with Chiwei's trait rebalance package against CMC 1.68.41 and adopted in 1.68.45.

- **2 new perk icons**: `CMC_Pk_Claws.png` (Claws, which borrowed the vanilla `Knife_Flint` sprite
  before) and `CMC_Pk_GiantStomach.png` (the new Giant Stomach perk; delivered as
  `CMC_PK_GiantStomach.png` and renamed to the suite's `CMC_Pk_` casing).
- **5 re-crops** replacing files from the 2026-09-22 drop under the same names:
  `CMC_Pk_Agoraphobia.png`, `CMC_Pk_Agoraphobia_TraitAgoraphobia_1.png`, `CMC_Pk_Nyctophobia.png`,
  `CMC_Pk_Nyctophobia_Trait_1-2.png` and `CMC_Pk_WeakStomach.png`.

All seven arrived as RGBA with the background keyed, 200 to 220px square, and ship as received.
The same package moved every harmful Sunlight Exposure band onto
`CMC_Pk_Nightcrawler_TraitNightcrawlerSun_2.png`, so `_3`, `_4` and `_5` from the 2026-09-22 drop
were no longer referenced by any status and were removed in 1.68.45. 50 of the 53 files from that
drop still ship.

## Generated in-repo (26 files)

Card art generated through `/create-image` (OpenArt, model `nano-banana-2`) and finished with
`Development_Tools/CardArt/process_card_art.py`. Each carries a sidecar under `.art/` recording its
subject, prompt, model, parameters, generation date and credit cost.

## Not recorded here (105 files)

Older card art predating the `.art/` sidecar convention. Its provenance is not documented in this
repository; this section is a statement of what is missing, not a claim that the files are
unencumbered. Record a file's source here when you next touch it.

## Terms

Chiwei granted free use of the 2026-09-22 artwork in the Raptor Studios CSFF mod suite,
including its public Nexus releases, with attribution. Confirmed by the maintainer 2026-09-22.
Attribute as "Artwork by Chiwei". This grant covers use within the suite; it is not a transfer
of copyright and does not license the artwork for use outside these mods. The maintainer
confirmed on 2026-09-26 that the same terms and attribution cover the 2026-09-24 icons. The terms above apply only
to Chiwei's files; the 105 files in the section above remain unrecorded.

## Adding artwork

When adding external, commissioned, AI-generated, or derived artwork, record the source,
author/tool, licence or terms, and any attribution text here **in the same commit** that adds the
file.
