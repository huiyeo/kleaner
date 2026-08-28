"""Build a review sheet for the Kleaner icon: light/dark rows, native small
sizes, zoom strip, color variants. Also verifies every ICO frame decodes."""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).parent
sys.path.insert(0, str(HERE))
import make_icon  # noqa: E402

LIGHT, DARK = (243, 244, 246), (32, 33, 38)
W, SECTION = 1180, 300

sheet = Image.new("RGB", (W, SECTION * 2 + 150), LIGHT)
draw = ImageDraw.Draw(sheet)
sheet.paste(Image.new("RGB", (W, SECTION)), (0, SECTION), )


def paste_icon(im: Image.Image, x: int, y: int) -> None:
    sheet.paste(im, (x, y), im)


for row, bg in ((0, LIGHT), (1, DARK)):
    y0 = row * SECTION
    if row == 1:
        sheet.paste(Image.new("RGB", (W, SECTION)), (0, y0))
    ico = Image.open(HERE / "Kleaner.ico")
    paste_icon(Image.open(HERE / "Kleaner-1024.png").resize((232, 232), Image.LANCZOS), 30, y0 + 34)
    x = 300
    for n in (48, 32, 24, 16):
        ico.size = (n, n)
        paste_icon(ico.convert("RGBA"), x, y0 + 34 + (232 - n) // 2)
        x += n + 46
    paste_icon(Image.open(HERE / "variant_mint_256.png").resize((232, 232), Image.LANCZOS), 600, y0 + 34)
    paste_icon(Image.open(HERE / "variant_navy_256.png").resize((232, 232), Image.LANCZOS), 880, y0 + 34)

# zoom strip: 16/24/32 frames upscaled 6x
zy = SECTION * 2 + 20
x = 30
for n in (16, 24, 32):
    ico = Image.open(HERE / "Kleaner.ico")
    ico.size = (n, n)
    up = ico.convert("RGBA").resize((n * 6, n * 6), Image.NEAREST)
    sheet.paste(up, (x, zy), up)
    draw.text((x, zy + n * 6 + 6), f"{n}px x6", fill=(90, 90, 96))
    x += n * 6 + 40

sheet.save(HERE / "预览_Kleaner图标_20260828.png")

# verify: every frame decodes and carries opaque pixels
for n in make_icon.SIZES:
    ico = Image.open(HERE / "Kleaner.ico")
    ico.size = (n, n)
    alpha = ico.convert("RGBA").getchannel("A")
    assert alpha.getextrema()[1] == 255 and alpha.getbbox(), f"frame {n} broken"
print("preview written; all", len(make_icon.SIZES), "ico frames verified")
