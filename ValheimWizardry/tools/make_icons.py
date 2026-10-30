"""Turns the 256 px Blender renders (make_models.py) into 128 px inventory icons with a soft shadow.

    python make_icons.py <renders dir> <assets dir> [codex render]
"""
import os
import sys

from PIL import Image, ImageFilter

src, out = sys.argv[1], sys.argv[2]
for f in sorted(os.listdir(src)):
    if not f.startswith("icon_") or not f.endswith(".png"):
        continue
    im = Image.open(os.path.join(src, f)).convert("RGBA")
    a = im.getchannel("A")
    shadow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    shadow.putalpha(a.filter(ImageFilter.GaussianBlur(6)).point(lambda v: int(v * 0.55)))
    base = Image.new("RGBA", im.size, (0, 0, 0, 0))
    base.alpha_composite(shadow, (3, 5))
    base.alpha_composite(im)
    small = base.resize((128, 128), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.2, percent=60, threshold=2))
    small.save(os.path.join(out, f), optimize=True)
if len(sys.argv) > 3:
    Image.open(sys.argv[3]).convert("RGBA").resize((128, 128), Image.LANCZOS).save(os.path.join(out, "icon_codex.png"), optimize=True)
print("icons written to", out)
