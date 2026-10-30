#!/usr/bin/env python3
"""Draw the heated Metal Sheet and Metal Nail pictures from the framework's own cold ones, given a forge glow.

Framework 2.29.0 gives the Metal Sheet and the Metal Nail a heated state (fleet plan 5.1, finding
Documentation/Research/Fleet/APE_Port/W0.6_metal_parts.md): above Temperature 150 the card is named and drawn as
heated, as vanilla's metal bars are. Chiwei's Additional Processing Equipment ships heated pictures of its own
sheet and nails, but no grant covers those files (see ../ASSET_CREDITS.md, the same reason the Stearin Cake is
not her file), so the framework draws its heated pictures from its own cold ones: Chiwei's 0928 set, which the
grant does cover. No credits are spent.

Every opaque pixel that is not ink (value <= INK_V) is mapped through a forge-glow ramp by its brightness (dark
red, then orange, then a pale yellow at the highlights) and blended over the original by GLOW, so the metal's
own shading and the ink outline still read. Alpha is untouched. The result goes through trim_card_art.trim()
onto the 512x768 card canvas (the subject keeps its vanilla placement) and is stored as a palette PNG.

    python CSFFModFramework/.art/make_heated_metal.py            # writes the four pictures
    python CSFFModFramework/.art/make_heated_metal.py --check    # verifies each output exists, prints sizes

Outputs (Resource/Picture): ACT_MetalSheet_Copper_Heated, ACT_MetalSheet_GhostBronze_Heated,
ACT_MetalNails_Copper_Heated, ACT_MetalNails_GhostBronze_Heated. The other four metals' heated rows reuse the
copper one, as Chiwei's own mod does (W0.6 choice D5, tunable).
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PIC = os.path.join(HERE, "..", "Resource", "Picture")
sys.path.insert(0, os.path.join(HERE, "..", "..", "Development_Tools", "CardArt"))
import trim_card_art  # noqa: E402

INK_V = 0.22   # value at or below this is the ink outline, left as drawn
GLOW = 0.72    # share of the glow colour in each metal pixel
# Forge-glow ramp by brightness: (brightness, RGB 0-1)
RAMP = [(0.00, (0.35, 0.03, 0.01)), (0.35, (0.72, 0.10, 0.02)), (0.65, (0.96, 0.38, 0.05)),
        (0.85, (1.00, 0.68, 0.22)), (1.00, (1.00, 0.93, 0.70))]

PAIRS = [("ACT_MetalSheet_Copper", "ACT_MetalSheet_Copper_Heated"),
         ("ACT_MetalSheet_GhostBronze", "ACT_MetalSheet_GhostBronze_Heated"),
         ("ACT_MetalNails_Copper", "ACT_MetalNails_Copper_Heated"),
         ("ACT_MetalNails_GhostBronze", "ACT_MetalNails_GhostBronze_Heated")]


def ramp(v):
    xs = np.array([p[0] for p in RAMP])
    cols = np.array([p[1] for p in RAMP])
    return np.stack([np.interp(v, xs, cols[:, c]) for c in range(3)], axis=-1)


def glow(im):
    a = np.asarray(im.convert("RGBA")).astype(np.float64) / 255.0
    rgb, al = a[..., :3], a[..., 3]
    v = rgb.max(-1)
    metal = (al > 0) & (v > INK_V)
    # stretch the metal's own brightness range onto the ramp so dull metals glow as brightly as copper
    lo, hi = np.percentile(v[metal], 2), np.percentile(v[metal], 98)
    t = np.clip((v - lo) / max(hi - lo, 1e-6), 0, 1)
    hot = ramp(t)
    out = a.copy()
    out[..., :3] = np.where(metal[..., None], rgb * (1 - GLOW) + hot * GLOW, rgb)
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA")


def main():
    check = "--check" in sys.argv[1:]
    for src, dst in PAIRS:
        out = os.path.join(PIC, dst + ".png")
        if not check:
            img = trim_card_art.trim(glow(Image.open(os.path.join(PIC, src + ".png"))))[0]
            trim_card_art.save(img, out)
        if not os.path.isfile(out):
            raise SystemExit(f"missing {out}")
        im = Image.open(out)
        print(f"{dst}.png: {im.size[0]}x{im.size[1]} {im.mode} {os.path.getsize(out) // 1024} KB")


if __name__ == "__main__":
    main()
