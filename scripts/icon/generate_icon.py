#!/usr/bin/env python3
"""
Generates the SystemInfo application icon (uniform "Quick Scaler" family layout).

Usage:
    python scripts/icon/generate_icon.py

Writes:
    Assets/SystemInfo.ico  - multi-resolution icon (16/24/32/48/64/128/256)
    docs/icon.png          - 1024 px master (README / design reference)

Requires: Python >= 3.10, Pillow, numpy.
Design: deep-navy rounded squircle with thin periwinkle inset outline,
        bold off-white "SI" monogram above a teal->green equalizer strip.
Matches the Crispy Bills / Dupe Check / Quick Scaler family icons.
"""

import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ICO_OUT = os.path.join(ROOT, "Assets", "SystemInfo.ico")
PNG_OUT = os.path.join(ROOT, "docs", "icon.png")

S = 1024
RX = 225
IX, IW = 17, 7
BG_TOP, BG_BOT = (27, 30, 51), (21, 24, 41)
OUTLINE = (72, 78, 112)
GLYPH = (226, 232, 244)
TEAL, GREEN = (63, 220, 190), (70, 223, 156)
FONTS = r"C:\Windows\Fonts"
ARIBLK = os.path.join(FONTS, "ariblk.ttf")


def background():
    t = np.linspace(0, 1, S, dtype=np.float32)[:, None, None]
    top = np.array(BG_TOP, np.float32)
    bot = np.array(BG_BOT, np.float32)
    grad = np.repeat((top[None, None, :] * (1 - t) + bot[None, None, :] * t).astype(np.uint8), S, axis=1)
    img = Image.fromarray(grad, "RGB").convert("RGBA")
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=RX, fill=255)
    img.putalpha(mask)
    ImageDraw.Draw(img).rounded_rectangle([IX, IX, S - 1 - IX, S - 1 - IX],
                                          radius=RX - IX, outline=OUTLINE, width=IW)
    return img


def tick_row():
    heights, w, gap, y_bot = (60, 108, 150, 168, 88), 70, 38, 908
    total = len(heights) * w + (len(heights) - 1) * gap
    mask = Image.new("L", (S, S), 0)
    dm = ImageDraw.Draw(mask)
    x = (S - total) // 2
    for h in heights:
        dm.rounded_rectangle([x, y_bot - h, x + w, y_bot], radius=w // 2, fill=255)
        x += w + gap
    xs = (S - total) // 2
    xe = (S + total) // 2
    row = np.linspace(0, 1, xe - xs, dtype=np.float32)[:, None]
    a = np.array(TEAL, np.float32)[None, :]
    b = np.array(GREEN, np.float32)[None, :]
    grad = np.repeat((a * (1 - row) + b * row).astype(np.uint8)[None, :, :], S, axis=0)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(Image.fromarray(grad, "RGB").convert("RGBA"), (xs, 0))
    img.putalpha(mask)
    return img


base = background()

glyph = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(glyph)
font = ImageFont.truetype(ARIBLK, 290)
d.text((512, 360), "SI", font=font, fill=GLYPH, anchor="mm")

alpha = glyph.getchannel("A").filter(ImageFilter.GaussianBlur(16))
shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
black = Image.new("RGBA", (S, S), (0, 0, 0, 255))
shadow.paste(black, (0, 9), alpha.point(lambda a: int(a * 0.45)))
base.alpha_composite(shadow)
base.alpha_composite(glyph)
base.alpha_composite(tick_row())

base.save(PNG_OUT)
base.save(ICO_OUT, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(f"wrote {PNG_OUT}")
print(f"wrote {ICO_OUT}")
