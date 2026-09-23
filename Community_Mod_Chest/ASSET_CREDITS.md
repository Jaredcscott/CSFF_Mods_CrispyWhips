# Asset Credits

Artwork shipped in this package, with its source and terms. Counts measured 2026-09-22 against
`Resource/Picture/` (184 PNG files).

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
of copyright and does not license the artwork for use outside these mods. The terms above apply only
to Chiwei's files; the 105 files in the section above remain unrecorded.

## Adding artwork

When adding external, commissioned, AI-generated, or derived artwork, record the source,
author/tool, licence or terms, and any attribution text here **in the same commit** that adds the
file.
