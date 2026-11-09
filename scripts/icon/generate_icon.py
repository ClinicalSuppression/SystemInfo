#!/usr/bin/env python3
"""
Generates the SystemInfo application icon.

Usage:
    python scripts/icon/generate_icon.py

Writes:
    Assets/SystemInfo.ico  - multi-resolution icon (16/24/32/48/64/128/256)
    docs/icon.png          - 1024 px master (README / design reference)

Requires: Python >= 3.10, Pillow, numpy.
Design: rounded dark badge with "SI" monogram and a teal->green equalizer strip below.
"""

import io
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ICO_OUT = os.path.join(ROOT, "Assets", "SystemInfo.ico")
PNG_OUT = os.path.join(ROOT, "docs", "icon.png")

S = 1024
SS = 3
W = S * SS

BADGE_TOP = (27, 30, 52)
BADGE_BOTTOM = (12, 15, 30)
HAIRLINE = (72, 78, 112, 255)
RADIUS = 232 * SS
TEAL = (64, 224, 214)
GREEN = (74, 222, 128)
FG = (226, 232, 244, 255)


def vgrad(w, h, top, bottom):
    ramp = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None, None]
    img = (np.array(top, dtype=np.float32) * (1 - ramp)
           + np.array(bottom, dtype=np.float32) * ramp)
    return Image.fromarray(np.repeat(img, w, axis=1).astype(np.uint8), "RGB")


def hgrad_rgb(w, h, left, right):
    ramp = np.linspace(0.0, 1.0, w, dtype=np.float32)[None, :, None]
    img = (np.array(left, dtype=np.float32) * (1 - ramp)
           + np.array(right, dtype=np.float32) * ramp)
    return Image.fromarray(np.repeat(img, h, axis=0).astype(np.uint8), "RGB")


def badge():
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    bg = vgrad(W, W, BADGE_TOP, BADGE_BOTTOM).convert("RGBA")
    mask = Image.new("L", (W, W), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([0, 0, W - 1, W - 1], radius=RADIUS, fill=255)
    img.paste(bg, (0, 0), mask)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([7 * SS, 7 * SS, W - 1 - 7 * SS, W - 1 - 7 * SS],
                        radius=(RADIUS // SS - 7) * SS,
                        outline=HAIRLINE, width=5 * SS)
    return img


def draw_monogram(img):
    font_path = r"C:\Windows\Fonts\bahnschrift.ttf"
    font = ImageFont.truetype(font_path, 470 * SS)
    tmp = Image.new("RGBA", (W, W))
    td = ImageDraw.Draw(tmp)
    text = "SI"
    bbox = td.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    x = (W - tw) // 2 - bbox[0]
    y = int(W * 0.36) - th // 2 - bbox[1]
    # soft shadow layer
    shadow = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.text((x + 5 * SS, y + 8 * SS), text, font=font, fill=(2, 4, 12, 200))
    shadow = shadow.filter(ImageFilter.GaussianBlur(7 * SS))
    img = Image.alpha_composite(img, shadow)
    d = ImageDraw.Draw(img)
    d.text((x, y), text, font=font, fill=FG)
    return img


def draw_meter(img):
    grad = hgrad_rgb(W, 130 * SS, TEAL, GREEN).convert("RGBA")
    mask = Image.new("L", (W, 130 * SS), 0)
    md = ImageDraw.Draw(mask)
    seg_w, gap = 74 * SS, 33 * SS
    base_h = 130 * SS
    heights = [0.38, 0.62, 0.84, 1.0, 0.55]
    cx = (W - (seg_w * 5 + gap * 4)) // 2
    for i, k in enumerate(heights):
        h = int(base_h * k)
        md.rounded_rectangle(
            [cx + i * (seg_w + gap), base_h - h, cx + i * (seg_w + gap) + seg_w, base_h],
            radius=seg_w // 2, fill=255)
    ty = int(W * 0.655)
    img.paste(grad, (0, ty), mask)
    return img


def main():
    img = badge()
    img = draw_monogram(img)
    img = draw_meter(img)
    master = img.resize((S, S), Image.LANCZOS)

    os.makedirs(os.path.dirname(PNG_OUT), exist_ok=True)
    master.save(PNG_OUT)

    png_buf = io.BytesIO()
    master.save(png_buf, format="PNG")
    os.makedirs(os.path.dirname(ICO_OUT), exist_ok=True)
    Image.open(png_buf).convert("RGBA").save(
        ICO_OUT, format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", PNG_OUT)
    print("wrote", ICO_OUT)


if __name__ == "__main__":
    main()
