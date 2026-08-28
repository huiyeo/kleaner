"""Generate the Kleaner application icon (multi-resolution .ico + PNG masters).

Design: rounded-square tile, vertical gradient, bold white "K",
sparkle cluster (clean/shine semantics). Drawn at 4x supersampling.

Usage:
    python make_icon.py            # writes Kleaner.ico + master PNGs + variants
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).parent
BASE = 1024          # master design size
SS = 4               # supersampling factor
FONT_CANDIDATES = ["ariblk.ttf", "segoeuib.ttf", "arialbd.ttf", "calibrib.ttf"]

# variant -> (gradient top, gradient bottom, sparkle color)
VARIANTS = {
    "blue":  ((43, 200, 255), (8, 87, 232),  (255, 255, 255)),
    "mint":  ((74, 227, 181), (10, 165, 116), (255, 255, 255)),
    "navy":  ((63, 76, 99),  (23, 32, 56),   (103, 232, 249)),
}

# ICO size ladder; per-tier drawing tweaks keep small sizes legible
SIZES = [16, 24, 32, 48, 64, 128, 256]


def load_font(target_cap: int) -> ImageFont.FreeTypeFont:
    """Pick the first available bold font, sized so "K" cap height == target_cap."""
    path = next((p for p in FONT_CANDIDATES
                 if (Path("C:/Windows/Fonts") / p).exists()), None)
    if path is None:
        raise RuntimeError("no bold font found in C:/Windows/Fonts")
    size = 100
    probe = ImageFont.truetype(str(Path("C:/Windows/Fonts") / path), size)
    bbox = ImageDraw.Draw(Image.new("L", (4, 4))).textbbox((0, 0), "K", font=probe)
    return ImageFont.truetype(str(Path("C:/Windows/Fonts") / path),
                              round(size * target_cap / (bbox[3] - bbox[1])))


def sparkle(draw: ImageDraw.ImageDraw, cx: int, cy: int, r: int, color) -> None:
    """Four-pointed star (concave diamond)."""
    d = round(r * 0.22)
    pts = [(cx, cy - r), (cx + d, cy - d), (cx + r, cy), (cx + d, cy + d),
           (cx, cy + r), (cx - d, cy + d), (cx - r, cy), (cx - d, cy - d)]
    draw.polygon(pts, fill=color)


def diagonal_gradient(size: int, top, bottom) -> Image.Image:
    """Diagonal (top-left -> bottom-right) two-color gradient."""
    small = Image.new("RGB", (64, 64))
    px = small.load()
    for y in range(64):
        for x in range(64):
            t = (x + y) / 126
            px[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(top, bottom))
    return small.resize((size, size), Image.BILINEAR)


def render(size: int, variant: str = "blue") -> Image.Image:
    """Render the full design at `size` px (drawn at SS*s and downsampled)."""
    s = size * SS
    top, bottom, spk = VARIANTS[variant]

    tile = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    radius = round(s * 0.223)

    # gradient background clipped to rounded square
    bg = diagonal_gradient(s, top, bottom).convert("RGBA")
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius, fill=255)
    tile.paste(bg, (0, 0), mask)

    # soft top sheen for depth
    sheen = Image.new("L", (s, s), 0)
    ImageDraw.Draw(sheen).ellipse(
        [-s * 0.25, -s * 0.45, s * 1.25, s * 0.62], fill=46)
    sheen = sheen.filter(ImageFilter.GaussianBlur(s * 0.06))
    tile.paste(Image.new("RGBA", (s, s), (255, 255, 255, 255)), (0, 0),
               Image.composite(sheen, Image.new("L", (s, s), 0), mask))

    layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)

    big_sparkle = s > 32 * SS      # 16 px keeps a single, larger sparkle
    spk_scale = 1.0 if size >= 48 else (1.15 if size >= 32 else 1.55)

    # letter K, slightly left of center to balance the sparkle
    cap = round(s * (0.445 if big_sparkle else 0.50))
    font = load_font(cap)
    bb = draw.textbbox((0, 0), "K", font=font)
    kx = round(s * 0.462) - (bb[0] + bb[2]) // 2
    ky = round(s * 0.535) - (bb[1] + bb[3]) // 2

    # soft drop shadow under the letter
    shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).text((kx, ky + s * 0.012), "K", font=font,
                                fill=(0, 20, 60, 90))
    tile.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(s * 0.012)))

    draw.text((kx, ky), "K", font=font, fill=(255, 255, 255, 255))

    # sparkle cluster, top-right (kept clear of the K's upper arm)
    r1 = round(s * 0.105 * spk_scale)
    c1 = (round(s * 0.768), round(s * 0.242))
    glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    sparkle(ImageDraw.Draw(glow), *c1, r=round(r1 * 1.25),
            color=(*spk, 110))
    tile.alpha_composite(glow.filter(ImageFilter.GaussianBlur(s * 0.018)))
    sparkle(draw, *c1, r=r1, color=(*spk, 255))
    if big_sparkle:
        sparkle(draw, round(s * 0.846), round(s * 0.422),
                r=round(s * 0.042 * spk_scale), color=(*spk, 235))

    tile.alpha_composite(layer)

    if size < 128:   # hairline edge keeps the tile crisp on light backgrounds
        ImageDraw.Draw(tile).rounded_rectangle(
            [0, 0, s - 1, s - 1], radius, outline=(255, 255, 255, 60),
            width=max(1, s // 512))

    return tile.resize((size, size), Image.LANCZOS)


def main() -> None:
    master = render(1024)
    master.save(HERE / "Kleaner-1024.png")

    frames = {n: render(n) for n in SIZES}
    ico_path = HERE / "Kleaner.ico"
    frames[256].save(
        ico_path, format="ICO",
        sizes=[(n, n) for n in SIZES],
        append_images=[frames[n] for n in SIZES if n != 256],
    )

    for v in ("mint", "navy"):
        render(256, v).save(HERE / f"variant_{v}_256.png")

    with Image.open(ico_path) as ico:
        print("ico frames:", sorted(ico.info.get("sizes", [])))
    print("done ->", ico_path)


if __name__ == "__main__":
    main()
