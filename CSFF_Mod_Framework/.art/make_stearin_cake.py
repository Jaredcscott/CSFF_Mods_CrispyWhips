#!/usr/bin/env python3
"""Draw the Stearin Cake card picture (CSFFMFW_StearinCake.png) from vanilla's own Fat sprite, recoloured to pale wax.

Chiwei's Additional Processing Equipment draws its Stearin Cake the same way (a whitened copy of vanilla's Fat
chunk), so the framework's picture is built the same way from vanilla's sprite rather than copied from her file
(no grant covers that file, see ../ASSET_CREDITS.md). No credits are spent.

Vanilla's Fat sprite is a yellow-brown chunk with a black ink outline, white glints and a grey cast shadow. The
body is every opaque pixel that is neither ink (value <= INK_V) nor the grey shadow (saturation <= SHADOW_S): its
saturation is cut to SAT_KEEP of the original and its value lifted toward white by LIFT, so the vanilla shading
still reads (the darkest browns become a warm grey-ivory, the glints stay white). Ink and shadow stay vanilla's.
The result goes through trim_card_art.trim() onto the 512x768 card canvas and is stored as a palette PNG.

    python CSFFModFramework/.art/make_stearin_cake.py <Fat.png> <out.png>

<Fat.png> is vanilla's sprite, exported with
    python Development_Tools/SpriteCheck/extract_vanilla_sprites.py export <dir> Fat
(UnityPy is not a repo dependency; see that file's notes). Output: CSFFModFramework/Resource/Picture/CSFFMFW_StearinCake.png.
"""
import colorsys
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Development_Tools", "CardArt"))
import trim_card_art  # noqa: E402

INK_V = 0.28        # value at or below this is the ink outline
SHADOW_S = 0.06     # saturation at or below this (and not near white) is the grey cast shadow
SAT_KEEP = 0.16     # share of the original saturation the wax keeps
LIFT = 0.55         # how far the value moves toward 1.0


def whiten(im):
    a = np.asarray(im.convert("RGBA")).astype(np.float64) / 255.0
    out = a.copy()
    h, w = a.shape[:2]
    for y in range(h):
        for x in range(w):
            r, g, b, al = a[y, x]
            if al <= 0.0:
                continue
            hh, s, v = colorsys.rgb_to_hsv(r, g, b)
            if v <= INK_V:
                continue                       # ink outline
            if s <= SHADOW_S and v < 0.9:
                continue                       # grey cast shadow
            s2 = s * SAT_KEEP
            v2 = v + (1.0 - v) * LIFT
            out[y, x, :3] = colorsys.hsv_to_rgb(hh, s2, v2)
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA")


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    src, dst = sys.argv[1], sys.argv[2]
    img = trim_card_art.trim(whiten(Image.open(src)))[0]
    trim_card_art.save(img, dst)
    print(f"{dst}: {img.size[0]}x{img.size[1]} {os.path.getsize(dst) // 1024} KB")


if __name__ == "__main__":
    main()
