"""Draws the mod's interface art into assets/: the open grimoire, tabs, buttons, panels, talent node frames, spell
glyphs, house crests and small signs. Pure Python (Pillow + numpy); run: python make_ui_art.py <assets dir>."""
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else "assets"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)
random.seed(7)


def save(img, name):
    img.save(os.path.join(OUT, name), optimize=True)


def noise(w, h, scale, octaves=4, seed=0):
    """Fractal value noise in 0..1."""
    r = np.random.default_rng(seed)
    acc = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cw, ch = max(2, int(w / scale * 2 ** o)), max(2, int(h / scale * 2 ** o))
        base = Image.fromarray((r.random((ch, cw)) * 255).astype(np.uint8))
        acc += np.asarray(base.resize((w, h), Image.BICUBIC), np.float32) / 255.0 * amp
        total += amp
        amp *= 0.5
    return np.clip(acc / total, 0, 1)


def rgba(arr_rgb, alpha=None):
    arr = np.clip(arr_rgb, 0, 255).astype(np.uint8)
    img = Image.fromarray(arr, "RGB").convert("RGBA")
    if alpha is not None:
        img.putalpha(Image.fromarray(np.clip(alpha, 0, 255).astype(np.uint8)))
    return img


def rounded_mask(w, h, r, ss=4):
    m = Image.new("L", (w * ss, h * ss), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, w * ss - 1, h * ss - 1], r * ss, fill=255)
    return m.resize((w, h), Image.LANCZOS)


def parchment(w, h, seed=1, base=(234, 219, 182)):
    n1 = noise(w, h, 260, 5, seed)
    n2 = noise(w, h, 40, 3, seed + 1)
    fib = noise(w, h, 6, 2, seed + 2)
    col = np.zeros((h, w, 3), np.float32)
    for i, c in enumerate(base):
        col[..., i] = c
    shade = (n1 - 0.5) * 38 + (n2 - 0.5) * 16 + (fib - 0.5) * 10
    col += shade[..., None] * np.array([1.0, 0.92, 0.75])
    # stains
    st = noise(w, h, 140, 3, seed + 3)
    stain = np.clip((st - 0.68) * 4, 0, 1)
    col -= stain[..., None] * np.array([28, 34, 40])
    # darker towards the edges
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    dx = np.minimum(xx, w - 1 - xx) / (w * 0.5)
    dy = np.minimum(yy, h - 1 - yy) / (h * 0.5)
    edge = np.clip(1 - np.minimum(dx, dy) * 3.2, 0, 1) ** 2
    col -= edge[..., None] * np.array([46, 52, 58])
    return col


def leather(w, h, seed=11, base=(74, 30, 24)):
    n1 = noise(w, h, 180, 5, seed)
    grain = noise(w, h, 3, 2, seed + 1)
    cells = noise(w, h, 9, 1, seed + 2)
    col = np.zeros((h, w, 3), np.float32)
    for i, c in enumerate(base):
        col[..., i] = c
    col += ((n1 - 0.5) * 34 + (grain - 0.5) * 22 + (np.abs(cells - 0.5) < 0.04) * -12)[..., None]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    v = ((xx / w - 0.5) ** 2 + (yy / h - 0.5) ** 2)
    col *= (1 - v * 0.9)[..., None]
    return col


GOLD = (212, 170, 92)
INK = (70, 42, 20)


def gold_corner(draw, x, y, sx, sy, s):
    """A metal corner protector pointing into the book from (x, y); sx, sy = +-1."""
    pts = [(x, y), (x + sx * s, y), (x, y + sy * s)]
    draw.polygon(pts, fill=(150, 112, 52))
    inner = [(x + sx * 6, y + sy * 6), (x + sx * (s - 18), y + sy * 6), (x + sx * 6, y + sy * (s - 18))]
    draw.polygon(inner, fill=GOLD)
    draw.line([pts[1], pts[2]], fill=(110, 78, 34), width=4)
    for k in (0.28, 0.55):
        cx, cy = x + sx * s * k * 0.55, y + sy * s * k * 0.55
        draw.ellipse([cx - 6, cy - 6, cx + 6, cy + 6], fill=(240, 214, 150), outline=(110, 78, 34), width=2)


def flourish(draw, cx, cy, size, color, flip_x=1, flip_y=1):
    for i in range(3):
        r = size * (0.35 + 0.22 * i)
        bbox = [cx - r, cy - r, cx + r, cy + r]
        start = 0 if flip_x > 0 else 90
        if flip_y < 0:
            start = 270 if flip_x > 0 else 180
        draw.arc(bbox, start, start + 90, fill=color, width=2)
    draw.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=color)


# ---------------------------------------------------------------------------------------------------- the open book
def book():
    W, H = 1600, 1000
    cover = rgba(leather(W, H), None)
    cover.putalpha(rounded_mask(W, H, 46))
    d = ImageDraw.Draw(cover)
    # stitching
    for i in range(0, W - 120, 22):
        d.line([(60 + i, 22), (72 + i, 22)], fill=(170, 130, 90), width=3)
        d.line([(60 + i, H - 23), (72 + i, H - 23)], fill=(170, 130, 90), width=3)
    for j in range(0, H - 120, 22):
        d.line([(22, 60 + j), (22, 72 + j)], fill=(170, 130, 90), width=3)
        d.line([(W - 23, 60 + j), (W - 23, 72 + j)], fill=(170, 130, 90), width=3)
    # page stacks
    px0, py0, px1, py1 = 58, 44, W - 58, H - 44
    for k in range(6, 0, -1):
        shade = 150 + k * 10
        d.rounded_rectangle([px0 - k * 2, py0 + k, W // 2, py1 + k * 2], 10, fill=(shade, shade - 18, shade - 52))
        d.rounded_rectangle([W // 2, py0 + k, px1 + k * 2, py1 + k * 2], 10, fill=(shade, shade - 18, shade - 52))
    # pages
    for side in (0, 1):
        x0 = px0 if side == 0 else W // 2 + 6
        x1 = W // 2 - 6 if side == 0 else px1
        pw, ph = x1 - x0, py1 - py0
        col = parchment(pw, ph, seed=3 + side)
        xx = np.linspace(0, 1, pw, dtype=np.float32)
        gutter = (xx if side == 0 else 1 - xx)
        curl = np.clip((gutter - 0.82) / 0.18, 0, 1) ** 1.6 * 70 + np.clip((0.06 - gutter) / 0.06, 0, 1) * 20
        col -= curl[None, :, None] * np.array([1.0, 1.0, 0.9])
        page = rgba(col, rounded_mask(pw, ph, 12) if False else None)
        mask = rounded_mask(pw, ph, 10)
        cover.paste(page, (x0, py0), mask)
        pd = ImageDraw.Draw(cover)
        # double rule with flourishes
        m = 34
        pd.rectangle([x0 + m, py0 + m, x1 - m, py1 - m], outline=(128, 92, 52), width=2)
        pd.rectangle([x0 + m + 6, py0 + m + 6, x1 - m - 6, py1 - m - 6], outline=(150, 112, 70), width=1)
        for (cx, cy, fx, fy) in ((x0 + m, py0 + m, 1, 1), (x1 - m, py0 + m, -1, 1), (x0 + m, py1 - m, 1, -1), (x1 - m, py1 - m, -1, -1)):
            pd.ellipse([cx - 7, cy - 7, cx + 7, cy + 7], fill=(140, 100, 56))
            pd.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], fill=(226, 200, 150))
    # spine shadow
    sh = Image.new("L", (W, H), 0)
    sd = ImageDraw.Draw(sh)
    sd.rectangle([W // 2 - 26, py0, W // 2 + 26, py1], fill=150)
    sh = sh.filter(ImageFilter.GaussianBlur(18))
    dark = Image.new("RGBA", (W, H), (40, 22, 10, 255))
    cover = Image.composite(dark, cover, sh.point(lambda v: int(v * 0.75)))
    cover.putalpha(ImageChops.multiply(cover.getchannel("A"), rounded_mask(W, H, 46)))
    d = ImageDraw.Draw(cover)
    d.line([(W // 2, py0), (W // 2, py1)], fill=(96, 64, 34), width=2)
    for (x, y, sx, sy) in ((0, 0, 1, 1), (W, 0, -1, 1), (0, H, 1, -1), (W, H, -1, -1)):
        gold_corner(d, x, y, sx, sy, 120)
    # ribbon bookmark hanging from the bottom
    rb = [(W // 2 + 40, py1 - 6), (W // 2 + 74, py1 - 6), (W // 2 + 74, H + 0), (W // 2 + 57, H - 18), (W // 2 + 40, H)]
    d.polygon(rb, fill=(120, 24, 30))
    d.line([rb[0], rb[4]], fill=(80, 12, 18), width=2)
    save(cover, "ui_book.png")


# ---------------------------------------------------------------------------------------------------- 9-slice pieces
def nine(name, w, h, fill_fn, border_col, border_w, radius, inner=None, alpha=255, glow=None):
    ss = 4
    big = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
    col = fill_fn(w * ss, h * ss)
    body = rgba(col)
    mask = Image.new("L", (w * ss, h * ss), 0)
    ImageDraw.Draw(mask).rounded_rectangle([2 * ss, 2 * ss, w * ss - 2 * ss - 1, h * ss - 2 * ss - 1], radius * ss, fill=alpha)
    big.paste(body, (0, 0), mask)
    d = ImageDraw.Draw(big)
    d.rounded_rectangle([2 * ss, 2 * ss, w * ss - 2 * ss - 1, h * ss - 2 * ss - 1], radius * ss, outline=border_col, width=border_w * ss)
    if inner:
        o = (border_w + 3) * ss
        d.rounded_rectangle([o, o, w * ss - o - 1, h * ss - o - 1], max(1, radius - 3) * ss, outline=inner, width=ss)
    img = big.resize((w, h), Image.LANCZOS)
    if glow:
        g = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        gm = img.getchannel("A").filter(ImageFilter.GaussianBlur(4))
        g.paste(Image.new("RGBA", (w, h), glow), (0, 0), gm)
        img = Image.alpha_composite(g, img)
    save(img, name)


def flat(c, n=0.0, seed=5):
    def f(w, h):
        arr = np.zeros((h, w, 3), np.float32) + np.array(c, np.float32)
        if n:
            arr += ((noise(w, h, 60, 3, seed) - 0.5) * n)[..., None]
        yy = np.linspace(0, 1, h, dtype=np.float32)[:, None, None]
        return arr * (1.08 - 0.16 * yy)
    return f


def pieces():
    nine("ui_btn.png", 128, 44, flat((214, 190, 142), 18), (110, 74, 38), 2, 8, inner=(236, 214, 170))
    nine("ui_btn_hover.png", 128, 44, flat((236, 214, 160), 18), (160, 110, 40), 2, 8, inner=(255, 236, 190))
    nine("ui_btn_off.png", 128, 44, flat((178, 168, 146), 10), (128, 116, 96), 2, 8)
    nine("ui_btn_red.png", 128, 44, flat((140, 30, 30), 26), (210, 160, 80), 2, 8, inner=(190, 70, 60))
    nine("ui_btn_red_hover.png", 128, 44, flat((172, 40, 36), 26), (236, 196, 110), 2, 8, inner=(220, 96, 80))
    nine("ui_tab.png", 200, 56, flat((92, 40, 30), 30), (150, 110, 60), 2, 10, inner=(120, 60, 44))
    nine("ui_tab_on.png", 200, 56, flat((150, 40, 36), 30), (230, 190, 110), 3, 10, inner=(200, 90, 70))
    nine("ui_panel.png", 96, 96, flat((24, 17, 12), 12), (150, 112, 60), 2, 12, inner=(70, 50, 30), alpha=232)
    nine("ui_note.png", 96, 96, lambda w, h: parchment(w, h, 21), (120, 84, 44), 2, 10, inner=(200, 172, 120))
    nine("ui_card.png", 96, 96, flat((226, 208, 166), 22), (150, 112, 70), 2, 10, inner=(240, 224, 186))
    nine("ui_card_on.png", 96, 96, flat((240, 222, 176), 22), (190, 120, 40), 3, 10, inner=(255, 236, 190))
    nine("ui_bar_bg.png", 64, 20, flat((60, 44, 30), 10), (120, 88, 50), 2, 6)
    nine("ui_bar_fill.png", 64, 20, flat((200, 150, 60), 14), (240, 200, 120), 1, 6)
    nine("ui_chip.png", 64, 32, flat((205, 184, 140), 12), (130, 96, 56), 2, 12)
    nine("ui_chip_on.png", 64, 32, flat((120, 36, 32), 16), (220, 180, 100), 2, 12)


# ---------------------------------------------------------------------------------------------------- round pieces
def radial(size, fn):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    r = np.sqrt((xx - size / 2 + 0.5) ** 2 + (yy - size / 2 + 0.5) ** 2) / (size / 2)
    return fn(r, xx / size, yy / size)


def round_pieces():
    S = 128
    # disc: tinted at run time with the school's colour
    a = radial(S, lambda r, x, y: np.clip((1 - r) * 40, 0, 1) * 255)
    shade = radial(S, lambda r, x, y: 0.55 + 0.45 * np.clip(1.15 - r - (y - 0.5) * 0.6, 0, 1))
    col = np.dstack([shade * 255] * 3)
    save(rgba(col, a), "ui_disc.png")
    # rim: white ring with bevel
    ring = radial(S, lambda r, x, y: np.clip((1 - np.abs(r - 0.9) / 0.08), 0, 1))
    hl = radial(S, lambda r, x, y: 0.7 + 0.3 * np.clip(0.5 - y, -1, 1))
    save(rgba(np.dstack([hl * 255] * 3), ring * 255), "ui_ring.png")
    # glow
    g = radial(S, lambda r, x, y: np.clip(1 - r, 0, 1) ** 2.2 * 255)
    save(rgba(np.full((S, S, 3), 255.0), g), "ui_glow.png")
    # slot for the spell bar
    S2 = 128
    a = radial(S2, lambda r, x, y: np.clip((1 - r) * 30, 0, 1) * 235)
    base = radial(S2, lambda r, x, y: 18 + 30 * np.clip(1 - r, 0, 1))
    img = rgba(np.dstack([base * 1.2, base * 0.95, base * 0.7]), a)
    d = ImageDraw.Draw(img)
    d.ellipse([3, 3, S2 - 4, S2 - 4], outline=(176, 132, 70), width=5)
    d.ellipse([10, 10, S2 - 11, S2 - 11], outline=(90, 66, 36), width=2)
    save(img, "ui_slot.png")
    img = Image.new("RGBA", (S2, S2), (0, 0, 0, 0))
    gl = radial(S2, lambda r, x, y: np.clip(1 - np.abs(r - 0.92) / 0.14, 0, 1) ** 1.5 * 255)
    img = rgba(np.dstack([np.full((S2, S2), 255.0), np.full((S2, S2), 214.0), np.full((S2, S2), 120.0)]), gl)
    save(img, "ui_slot_sel.png")
    # talent node frames (grey, tinted at run time)
    for name, size, spikes in (("node_small.png", 64, 0), ("node_notable.png", 96, 8), ("node_key.png", 128, 12)):
        ss = 4
        big = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
        d = ImageDraw.Draw(big)
        c = size * ss / 2
        if spikes:
            pts = []
            for i in range(spikes * 2):
                ang = math.pi * i / spikes - math.pi / 2
                rr = c * (0.98 if i % 2 == 0 else 0.8)
                pts.append((c + rr * math.cos(ang), c + rr * math.sin(ang)))
            d.polygon(pts, fill=(150, 150, 150, 255))
        rr = c * (0.78 if spikes else 0.96)
        d.ellipse([c - rr, c - rr, c + rr, c + rr], fill=(210, 210, 210, 255))
        rr2 = rr * 0.8
        d.ellipse([c - rr2, c - rr2, c + rr2, c + rr2], fill=(40, 34, 30, 255))
        d.ellipse([c - rr2, c - rr2, c + rr2, c + rr2], outline=(120, 120, 120, 255), width=2 * ss)
        save(big.resize((size, size), Image.LANCZOS), name)
    # a soft line for the talent constellation
    w, h = 64, 16
    yy = np.linspace(-1, 1, h, dtype=np.float32)[:, None]
    a = np.clip(1 - np.abs(yy), 0, 1) ** 1.4 * 255 * np.ones((1, w))
    save(rgba(np.full((h, w, 3), 255.0), a), "ui_line.png")


# ---------------------------------------------------------------------------------------------------- glyphs
def glyph(name, draw_fn, size=128):
    ss = 4
    S = size * ss
    m = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(m)
    draw_fn(d, S)
    m = m.resize((size, size), Image.LANCZOS)
    glow = m.filter(ImageFilter.GaussianBlur(5)).point(lambda v: int(v * 0.7))
    a = ImageChops.lighter(m, glow)
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    img.putalpha(a)
    save(img, "glyph_" + name + ".png")


def star_pts(cx, cy, r1, r2, n, rot=-math.pi / 2):
    pts = []
    for i in range(n * 2):
        a = rot + math.pi * i / n
        r = r1 if i % 2 == 0 else r2
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def g_proj(d, S):
    c = S * 0.62
    d.ellipse([c - S * 0.13, S * 0.38 - S * 0.13, c + S * 0.13, S * 0.38 + S * 0.13], fill=255)
    for i, w in enumerate((0.09, 0.06, 0.035)):
        off = (i - 1) * S * 0.08
        d.line([(c - S * 0.05, S * 0.43 + off * 0.3), (S * 0.14, S * 0.84 + off)], fill=int(255 - i * 50), width=int(S * w))
    d.polygon(star_pts(c, S * 0.38, S * 0.24, S * 0.07, 4, 0.3), fill=255)


def g_aoe(d, S):
    c = S / 2
    d.polygon(star_pts(c, c, S * 0.46, S * 0.2, 8), fill=255)
    d.ellipse([c - S * 0.12, c - S * 0.12, c + S * 0.12, c + S * 0.12], fill=0)
    d.ellipse([c - S * 0.07, c - S * 0.07, c + S * 0.07, c + S * 0.07], fill=255)


def g_beam(d, S):
    d.polygon([(S * 0.1, S * 0.82), (S * 0.18, S * 0.9), (S * 0.86, S * 0.22), (S * 0.78, S * 0.14)], fill=255)
    d.polygon(star_pts(S * 0.8, S * 0.2, S * 0.18, S * 0.05, 4), fill=255)
    d.polygon(star_pts(S * 0.32, S * 0.32, S * 0.08, S * 0.025, 4), fill=200)
    d.polygon(star_pts(S * 0.68, S * 0.7, S * 0.07, S * 0.02, 4), fill=200)


def g_ctrl(d, S):
    c = S / 2
    for i in range(3):
        r = S * (0.12 + 0.12 * i)
        d.arc([c - r, c - r, c + r, c + r], 30 + i * 70, 300 + i * 70, fill=255, width=int(S * 0.06))
    d.ellipse([c - S * 0.05, c - S * 0.05, c + S * 0.05, c + S * 0.05], fill=255)


def g_summ(d, S):
    # a spirit animal's head: a stag
    c = S / 2
    d.ellipse([c - S * 0.16, S * 0.42, c + S * 0.16, S * 0.86], fill=255)
    d.polygon([(c - S * 0.16, S * 0.56), (c - S * 0.32, S * 0.46), (c - S * 0.14, S * 0.5)], fill=255)
    d.polygon([(c + S * 0.16, S * 0.56), (c + S * 0.32, S * 0.46), (c + S * 0.14, S * 0.5)], fill=255)
    for sx in (-1, 1):
        x0 = c + sx * S * 0.08
        d.line([(x0, S * 0.44), (c + sx * S * 0.22, S * 0.12)], fill=255, width=int(S * 0.045))
        d.line([(c + sx * S * 0.15, S * 0.26), (c + sx * S * 0.34, S * 0.2)], fill=255, width=int(S * 0.04))
        d.line([(c + sx * S * 0.19, S * 0.17), (c + sx * S * 0.12, S * 0.06)], fill=255, width=int(S * 0.035))


def g_buff(d, S):
    pts = [(S * 0.5, S * 0.08), (S * 0.86, S * 0.2), (S * 0.8, S * 0.6), (S * 0.5, S * 0.92), (S * 0.2, S * 0.6), (S * 0.14, S * 0.2)]
    d.polygon(pts, fill=255)
    inner = [(S * 0.5, S * 0.2), (S * 0.74, S * 0.28), (S * 0.7, S * 0.56), (S * 0.5, S * 0.78), (S * 0.3, S * 0.56), (S * 0.26, S * 0.28)]
    d.polygon(inner, fill=0)
    d.polygon(star_pts(S * 0.5, S * 0.47, S * 0.17, S * 0.06, 4), fill=255)


def g_heal(d, S):
    c = S / 2
    w = S * 0.14
    d.rounded_rectangle([c - w, S * 0.16, c + w, S * 0.84], int(S * 0.05), fill=255)
    d.rounded_rectangle([S * 0.16, c - w, S * 0.84, c + w], int(S * 0.05), fill=255)
    for (x, y) in ((0.2, 0.2), (0.82, 0.24), (0.78, 0.8)):
        d.polygon(star_pts(S * x, S * y, S * 0.07, S * 0.02, 4), fill=220)


def g_move(d, S):
    for i in range(3):
        x = S * (0.18 + i * 0.22)
        d.polygon([(x, S * 0.2), (x + S * 0.22, S * 0.5), (x, S * 0.8), (x + S * 0.08, S * 0.5)], fill=int(130 + i * 60))


def g_util(d, S):
    d.polygon(star_pts(S / 2, S / 2, S * 0.44, S * 0.12, 4), fill=255)
    d.polygon(star_pts(S / 2, S / 2, S * 0.3, S * 0.1, 4, -math.pi / 4), fill=255)
    d.ellipse([S * 0.44, S * 0.44, S * 0.56, S * 0.56], fill=0)


def g_ritual(d, S):
    c = S / 2
    r = S * 0.42
    d.ellipse([c - r, c - r, c + r, c + r], outline=255, width=int(S * 0.035))
    pts = [(c + r * 0.95 * math.cos(-math.pi / 2 + k * 4 * math.pi / 5), c + r * 0.95 * math.sin(-math.pi / 2 + k * 4 * math.pi / 5)) for k in range(6)]
    d.line(pts, fill=255, width=int(S * 0.03), joint="curve")


def g_grand(d, S):
    base = S * 0.72
    d.polygon([(S * 0.14, base), (S * 0.18, S * 0.3), (S * 0.34, S * 0.5), (S * 0.5, S * 0.18), (S * 0.66, S * 0.5),
               (S * 0.82, S * 0.3), (S * 0.86, base)], fill=255)
    d.rounded_rectangle([S * 0.14, base + S * 0.04, S * 0.86, base + S * 0.14], int(S * 0.03), fill=255)
    for x in (0.18, 0.5, 0.82):
        y = 0.3 if x != 0.5 else 0.18
        d.ellipse([S * (x - 0.05), S * (y - 0.07), S * (x + 0.05), S * (y + 0.03)], fill=255)


def g_dark(d, S):
    c = S / 2
    d.ellipse([c - S * 0.3, S * 0.12, c + S * 0.3, S * 0.66], fill=255)
    d.rounded_rectangle([c - S * 0.18, S * 0.54, c + S * 0.18, S * 0.84], int(S * 0.04), fill=255)
    for sx in (-1, 1):
        d.ellipse([c + sx * S * 0.13 - S * 0.09, S * 0.36, c + sx * S * 0.13 + S * 0.09, S * 0.52], fill=0)
    d.polygon([(c, S * 0.52), (c - S * 0.04, S * 0.6), (c + S * 0.04, S * 0.6)], fill=0)
    for k in range(-2, 3):
        x = c + k * S * 0.07
        d.line([(x, S * 0.7), (x, S * 0.84)], fill=0, width=int(S * 0.02))


def g_wand(d, S):
    d.line([(S * 0.16, S * 0.86), (S * 0.66, S * 0.36)], fill=255, width=int(S * 0.07))
    d.line([(S * 0.16, S * 0.86), (S * 0.3, S * 0.72)], fill=255, width=int(S * 0.11))
    d.polygon(star_pts(S * 0.74, S * 0.28, S * 0.22, S * 0.06, 4), fill=255)
    d.polygon(star_pts(S * 0.42, S * 0.2, S * 0.07, S * 0.02, 4), fill=210)
    d.polygon(star_pts(S * 0.84, S * 0.6, S * 0.06, S * 0.02, 4), fill=210)


def g_book(d, S):
    d.polygon([(S * 0.5, S * 0.3), (S * 0.12, S * 0.2), (S * 0.12, S * 0.78), (S * 0.5, S * 0.88)], fill=255)
    d.polygon([(S * 0.5, S * 0.3), (S * 0.88, S * 0.2), (S * 0.88, S * 0.78), (S * 0.5, S * 0.88)], fill=200)
    for k in range(3):
        y = S * (0.38 + k * 0.12)
        d.line([(S * 0.2, y - S * 0.02), (S * 0.42, y + S * 0.03)], fill=0, width=int(S * 0.02))
        d.line([(S * 0.58, y + S * 0.03), (S * 0.8, y - S * 0.02)], fill=0, width=int(S * 0.02))


def g_point(d, S):
    d.polygon(star_pts(S / 2, S / 2, S * 0.47, S * 0.15, 4), fill=255)
    d.polygon(star_pts(S / 2, S / 2, S * 0.26, S * 0.1, 4, -math.pi / 4), fill=255)


def g_lock(d, S):
    d.arc([S * 0.28, S * 0.14, S * 0.72, S * 0.6], 180, 360, fill=255, width=int(S * 0.09))
    d.line([(S * 0.32, S * 0.38), (S * 0.32, S * 0.46)], fill=255, width=int(S * 0.09))
    d.line([(S * 0.68, S * 0.38), (S * 0.68, S * 0.46)], fill=255, width=int(S * 0.09))
    d.rounded_rectangle([S * 0.18, S * 0.44, S * 0.82, S * 0.88], int(S * 0.06), fill=255)
    d.ellipse([S * 0.45, S * 0.56, S * 0.55, S * 0.66], fill=0)
    d.rectangle([S * 0.48, S * 0.62, S * 0.52, S * 0.76], fill=0)


def g_check(d, S):
    d.line([(S * 0.16, S * 0.52), (S * 0.4, S * 0.76), (S * 0.86, S * 0.24)], fill=255, width=int(S * 0.13), joint="curve")


def g_quest(d, S):
    d.rounded_rectangle([S * 0.2, S * 0.14, S * 0.8, S * 0.86], int(S * 0.08), fill=255)
    d.ellipse([S * 0.1, S * 0.08, S * 0.3, S * 0.24], fill=255)
    d.ellipse([S * 0.7, S * 0.76, S * 0.9, S * 0.92], fill=255)
    for k in range(4):
        y = S * (0.3 + k * 0.13)
        d.line([(S * 0.3, y), (S * (0.7 - (k == 3) * 0.16), y)], fill=0, width=int(S * 0.035))


def g_trophy(d, S):
    d.polygon([(S * 0.24, S * 0.16), (S * 0.76, S * 0.16), (S * 0.68, S * 0.52), (S * 0.5, S * 0.6), (S * 0.32, S * 0.52)], fill=255)
    d.arc([S * 0.08, S * 0.18, S * 0.34, S * 0.46], 90, 270, fill=255, width=int(S * 0.05))
    d.arc([S * 0.66, S * 0.18, S * 0.92, S * 0.46], 270, 90, fill=255, width=int(S * 0.05))
    d.rectangle([S * 0.46, S * 0.58, S * 0.54, S * 0.74], fill=255)
    d.rounded_rectangle([S * 0.3, S * 0.74, S * 0.7, S * 0.86], int(S * 0.03), fill=255)


def g_codex(d, S):
    g_book(d, S)
    d.polygon(star_pts(S * 0.5, S * 0.16, S * 0.12, S * 0.04, 4), fill=255)


GLYPHS = dict(proj=g_proj, aoe=g_aoe, beam=g_beam, ctrl=g_ctrl, summ=g_summ, buff=g_buff, heal=g_heal, move=g_move,
              util=g_util, ritual=g_ritual, grand=g_grand, dark=g_dark, wand=g_wand, book=g_book, point=g_point,
              lock=g_lock, check=g_check, quest=g_quest, trophy=g_trophy, codex=g_codex)


# ---------------------------------------------------------------------------------------------------- crests
HOUSES = {
    "gryffindor": ((150, 24, 28), (218, 170, 60), (240, 206, 110)),
    "ravenclaw": ((24, 46, 120), (150, 104, 60), (196, 160, 110)),
    "hufflepuff": ((226, 178, 40), (34, 30, 26), (196, 140, 58)),
    "slytherin": ((26, 96, 52), (170, 176, 184), (214, 220, 226)),
}


def crest(house):
    W, H = 256, 300
    ss = 4
    big = Image.new("RGBA", (W * ss, H * ss), (0, 0, 0, 0))
    a, b, metal = HOUSES[house]
    shield = [(W * 0.08, H * 0.06), (W * 0.92, H * 0.06), (W * 0.92, H * 0.5), (W * 0.5, H * 0.96), (W * 0.08, H * 0.5)]
    sh = [(x * ss, y * ss) for x, y in shield]
    m = Image.new("L", big.size, 0)
    ImageDraw.Draw(m).polygon(sh, fill=255)
    tex = noise(W * ss, H * ss, 300, 4, hash(house) % 100)
    left = np.zeros((H * ss, W * ss, 3), np.float32) + np.array(a, np.float32)
    right = np.zeros((H * ss, W * ss, 3), np.float32) + np.array(b, np.float32)
    xx = np.mgrid[0:H * ss, 0:W * ss][1]
    yy = np.mgrid[0:H * ss, 0:W * ss][0]
    split = ((xx < W * ss / 2) ^ (yy > H * ss * 0.46))[..., None]
    col = np.where(split, left, right) * (0.85 + 0.3 * tex[..., None])
    shade = 1.15 - 0.45 * (yy / (H * ss))
    col = col * shade[..., None]
    body = rgba(col)
    big.paste(body, (0, 0), m)
    d = ImageDraw.Draw(big)
    d.line(sh + [sh[0]], fill=metal, width=9 * ss, joint="curve")
    d.line(sh + [sh[0]], fill=(60, 44, 24), width=2 * ss, joint="curve")
    # the house's sign in metal with a shadow
    sign = Image.new("L", big.size, 0)
    sd = ImageDraw.Draw(sign)
    S = W * ss
    cx, cy = S / 2, H * ss * 0.46
    if house == "gryffindor":   # the sword
        sd.polygon([(cx - S * 0.05, cy - S * 0.3), (cx + S * 0.05, cy - S * 0.3), (cx + S * 0.035, cy + S * 0.28), (cx, cy + S * 0.36),
                    (cx - S * 0.035, cy + S * 0.28)], fill=255)
        sd.rounded_rectangle([cx - S * 0.2, cy - S * 0.34, cx + S * 0.2, cy - S * 0.28], int(S * 0.02), fill=255)
        sd.rectangle([cx - S * 0.03, cy - S * 0.46, cx + S * 0.03, cy - S * 0.34], fill=255)
        sd.ellipse([cx - S * 0.05, cy - S * 0.52, cx + S * 0.05, cy - S * 0.44], fill=255)
    elif house == "ravenclaw":  # an eagle's feather
        sd.polygon([(cx + S * 0.12, cy - S * 0.36), (cx + S * 0.2, cy - S * 0.2), (cx + S * 0.02, cy + S * 0.22), (cx - S * 0.12, cy + S * 0.3),
                    (cx - S * 0.08, cy + S * 0.14)], fill=255)
        sd.line([(cx - S * 0.16, cy + S * 0.4), (cx + S * 0.14, cy - S * 0.3)], fill=0, width=int(S * 0.015))
        sd.line([(cx - S * 0.16, cy + S * 0.4), (cx - S * 0.1, cy + S * 0.26)], fill=255, width=int(S * 0.02))
        for k in range(5):
            t = 0.15 + k * 0.15
            x0, y0 = cx - S * 0.1 + t * S * 0.24, cy + S * 0.24 - t * S * 0.58
            sd.line([(x0, y0), (x0 + S * 0.08, y0 + S * 0.02)], fill=0, width=int(S * 0.008))
    elif house == "hufflepuff":  # a badger's paw
        sd.ellipse([cx - S * 0.17, cy - S * 0.02, cx + S * 0.17, cy + S * 0.26], fill=255)
        for k, ang in enumerate((-58, -20, 20, 58)):
            r = S * 0.26
            x = cx + r * math.sin(math.radians(ang))
            y = cy + S * 0.02 - r * math.cos(math.radians(ang)) * 0.85
            sd.ellipse([x - S * 0.055, y - S * 0.07, x + S * 0.055, y + S * 0.06], fill=255)
    else:  # the serpent
        pts = []
        for i in range(60):
            t = i / 59
            pts.append((cx + math.sin(t * math.pi * 2.2) * S * 0.16, cy - S * 0.32 + t * S * 0.66))
        sd.line(pts, fill=255, width=int(S * 0.07), joint="curve")
        hx, hy = pts[0]
        sd.ellipse([hx - S * 0.07, hy - S * 0.06, hx + S * 0.07, hy + S * 0.05], fill=255)
        sd.ellipse([hx - S * 0.02, hy - S * 0.03, hx + S * 0.01, hy], fill=0)
        sd.line([(hx + S * 0.06, hy), (hx + S * 0.12, hy - S * 0.02)], fill=255, width=int(S * 0.012))
    outline = sign.filter(ImageFilter.MaxFilter(4 * ss + 1))
    big.paste(Image.new("RGBA", big.size, (24, 16, 8, 255)), (0, 0), outline)
    shadow = sign.filter(ImageFilter.GaussianBlur(6 * ss))
    big.paste(Image.new("RGBA", big.size, (20, 12, 6, 200)), (int(1.5 * ss), int(3 * ss)), shadow)
    metal_img = rgba(np.zeros((H * ss, W * ss, 3), np.float32) + np.array(metal, np.float32) * (0.8 + 0.4 * (1 - yy / (H * ss)))[..., None])
    big.paste(metal_img, (0, 0), sign)
    img = big.resize((W, H), Image.LANCZOS)
    save(img, "crest_" + house + ".png")


if __name__ == "__main__":
    book()
    pieces()
    round_pieces()
    for k, f in GLYPHS.items():
        glyph(k, f)
    for h in HOUSES:
        crest(h)
    print("ui art written to", OUT)
