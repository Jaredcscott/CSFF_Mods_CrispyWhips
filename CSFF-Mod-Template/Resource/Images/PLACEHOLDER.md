# Card Art Placement

Place your card art PNG files here: `Resource/Images/`

## Naming Convention

The filename (without `.png`) must match the value you set in `CardImageWarpData` in your JSON.

**Example:**
- JSON: `"CardImageWarpData": "TODO_ModName_MyHerb"`
- File: `Resource/Images/TODO_ModName_MyHerb.png`

## Recommended Format

- **Size:** 512×512 pixels (the game scales it automatically)
- **Format:** PNG with transparency (RGBA)
- **Background:** Solid white for card art that matches vanilla style
- **Sprite name:** Keep it unique to your mod — prefix with your mod's name to avoid conflicts with vanilla sprite names

## Vanilla Sprites

You can reference vanilla sprite names instead of providing your own PNG.
For example, `"CardImageWarpData": "Plank"` will use the vanilla Plank sprite.
A full list of vanilla sprite names is in the ebook's Appendix B.

## Perk Icons

Perk icon PNGs also go here. The `PerkIconWarpData` field in your perk JSON
references the PNG filename the same way. **Never put a card UniqueID or GUID
in PerkIconWarpData** — it must be a sprite name string.
